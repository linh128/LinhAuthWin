using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuthWin
{
    internal sealed class VaultSession : IDisposable
    {
        internal const int CurrentIterations = 600000;
        private const int Version = 2;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AuthWin vault v2");

        private readonly byte[] encryptionKey;
        private readonly byte[] authenticationKey;
        private readonly byte[] salt;
        private readonly int iterations;

        private VaultSession(byte[] encryptionKey, byte[] authenticationKey, byte[] salt, int iterations)
        {
            this.encryptionKey = encryptionKey;
            this.authenticationKey = authenticationKey;
            this.salt = salt;
            this.iterations = iterations;
        }

        internal static VaultSession Create(string password)
        {
            byte[] salt = RandomBytes(16);
            return Derive(password, salt, CurrentIterations);
        }

        internal static bool TryUnlock(string fileContents, string password, out VaultSession session, out string json)
        {
            VaultEnvelope envelope = ReadEnvelope(fileContents);
            session = Derive(password, Convert.FromBase64String(envelope.Salt), envelope.Iterations);
            try
            {
                if (!session.HasValidTag(envelope))
                {
                    session.Dispose();
                    session = null;
                    json = null;
                    return false;
                }
                json = session.DecryptAuthenticated(envelope);
                return true;
            }
            catch
            {
                session.Dispose();
                session = null;
                throw;
            }
        }

        internal string Encrypt(string json)
        {
            byte[] iv = RandomBytes(16);
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            byte[] ciphertext;
            using (Aes aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            VaultEnvelope envelope = new VaultEnvelope
            {
                Version = Version,
                Iterations = iterations,
                Salt = Convert.ToBase64String(salt),
                Iv = Convert.ToBase64String(iv),
                Ciphertext = Convert.ToBase64String(ciphertext)
            };
            envelope.Tag = Convert.ToBase64String(ComputeTag(envelope, iv, ciphertext));
            return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        internal string Decrypt(string fileContents)
        {
            return Decrypt(ReadEnvelope(fileContents));
        }

        private string Decrypt(VaultEnvelope envelope)
        {
            if (!HasValidTag(envelope))
                throw new CryptographicException("Incorrect password or damaged account file.");

            return DecryptAuthenticated(envelope);
        }

        private bool HasValidTag(VaultEnvelope envelope)
        {
            byte[] fileSalt = Convert.FromBase64String(envelope.Salt);
            byte[] iv = Convert.FromBase64String(envelope.Iv);
            byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            byte[] tag = Convert.FromBase64String(envelope.Tag);
            byte[] expectedTag = ComputeTag(envelope, iv, ciphertext);
            return envelope.Iterations == iterations && Equal(fileSalt, salt) && Equal(tag, expectedTag);
        }

        private string DecryptAuthenticated(VaultEnvelope envelope)
        {
            byte[] iv = Convert.FromBase64String(envelope.Iv);
            byte[] ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            using (Aes aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                    return Encoding.UTF8.GetString(plaintext);
                }
            }
        }

        internal static void Save(string path, VaultSession session, string json, bool backupLegacy = false)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string encrypted = session.Encrypt(json);
                File.WriteAllText(temporaryPath, encrypted, Encoding.UTF8);
                if (session.Decrypt(File.ReadAllText(temporaryPath, Encoding.UTF8)) != json)
                    throw new InvalidDataException("Could not verify the saved account file.");

                if (File.Exists(path))
                {
                    if (backupLegacy)
                    {
                        string backupPath = path + ".legacy-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                        File.Replace(temporaryPath, path, backupPath);
                        if (session.Decrypt(File.ReadAllText(path, Encoding.UTF8)) != json)
                            throw new InvalidDataException("Could not verify the migrated account file.");
                        File.Delete(backupPath);
                    }
                    else
                        File.Replace(temporaryPath, path, null);
                }
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        internal static void RemoveCompletedMigrationBackups(string path)
        {
            string directory = Path.GetDirectoryName(path);
            string pattern = Path.GetFileName(path) + ".legacy-*.bak";
            foreach (string backup in Directory.GetFiles(directory, pattern)) File.Delete(backup);
        }

        private static VaultSession Derive(string password, byte[] salt, int iterations)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] material = kdf.GetBytes(64);
                byte[] encryptionKey = new byte[32];
                byte[] authenticationKey = new byte[32];
                Buffer.BlockCopy(material, 0, encryptionKey, 0, 32);
                Buffer.BlockCopy(material, 32, authenticationKey, 0, 32);
                Array.Clear(material, 0, material.Length);
                return new VaultSession(encryptionKey, authenticationKey, salt, iterations);
            }
        }

        private byte[] ComputeTag(VaultEnvelope envelope, byte[] iv, byte[] ciphertext)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(envelope.Version);
                writer.Write(envelope.Iterations);
                writer.Write(salt);
                writer.Write(iv);
                writer.Write(ciphertext.Length);
                writer.Write(ciphertext);
                writer.Flush();
                using (var hmac = new HMACSHA256(authenticationKey))
                    return hmac.ComputeHash(stream.ToArray());
            }
        }

        private static VaultEnvelope ReadEnvelope(string fileContents)
        {
            VaultEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<VaultEnvelope>(fileContents, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (envelope == null || envelope.Version != Version || envelope.Iterations < 100000 || envelope.Iterations > 5000000 ||
                    Convert.FromBase64String(envelope.Salt).Length != 16 || Convert.FromBase64String(envelope.Iv).Length != 16 ||
                    Convert.FromBase64String(envelope.Tag).Length != 32 ||
                    Convert.FromBase64String(envelope.Ciphertext).Length == 0 || Convert.FromBase64String(envelope.Ciphertext).Length % 16 != 0)
                    throw new InvalidDataException("Unsupported or damaged account file.");
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is ArgumentNullException)
            {
                throw new InvalidDataException("Unsupported or damaged account file.", ex);
            }
            return envelope;
        }

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            int difference = left.Length ^ right.Length;
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        public void Dispose()
        {
            Array.Clear(encryptionKey, 0, encryptionKey.Length);
            Array.Clear(authenticationKey, 0, authenticationKey.Length);
        }

        private sealed class VaultEnvelope
        {
            public VaultEnvelope() { }

            public int Version { get; set; }
            public int Iterations { get; set; }
            public string Salt { get; set; }
            public string Iv { get; set; }
            public string Ciphertext { get; set; }
            public string Tag { get; set; }
        }
    }
}
