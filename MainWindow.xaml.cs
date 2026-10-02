using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using OtpNet;
using System.IO;
using System.Text.Json.Serialization;
using QRCoder;
using Microsoft.Win32;
using ZXing;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Web;
using static QRCoder.QRCodeGenerator;

namespace AuthWin
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ObservableCollection<Account> accounts = new ObservableCollection<Account>();
        DispatcherTimer timer = new DispatcherTimer();
        DispatcherTimer timer2 = new DispatcherTimer();
        DispatcherTimer secretCopiedTimer = new DispatcherTimer();
        VaultSession vaultSession;
        System.Drawing.Bitmap qrCodeImage;
        bool EditMode = false;
        int EditIndex = -1;
        string AccounsFile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\AuthWin\\accounts.json";
        string PrefsFile = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\AuthWin\\prefs.json";
        AccountSortMode sortMode = AccountSortMode.Added;
        bool sortUiReady = false;

        public MainWindow()
        {
            InitializeComponent();
            secretCopiedTimer.Interval = TimeSpan.FromSeconds(2);
            secretCopiedTimer.Tick += secretCopiedTimer_Tick;
        }

        protected override void OnClosed(EventArgs e)
        {
            vaultSession?.Dispose();
            base.OnClosed(e);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Hide();
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AccounsFile));
                if (!InitializeVault())
                {
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the account file. No data was changed.\n\n" + ex.Message,
                    "AuthWin", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }
            Show();

            for (int i = 0; i < accounts.Count; i++)
            {
                Account acc = accounts[i];
                GenerateCode(acc);
            }
            lbCodes.ItemsSource = accounts;
            LoadSortPreference();
            ApplySort();
            sortUiReady = true;
            timer.Tick += timer_Tick;
            timer.Interval = new TimeSpan(0, 0, 1);
            timer.Start();

            timer2.Tick += timer2_Tick;
            timer2.Interval = new TimeSpan(0, 0, 3);
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            lbCodes.Visibility = Visibility.Collapsed;
            pnlBottom.Visibility = Visibility.Collapsed;
            grdAdd.Visibility = Visibility.Visible;
            grdAddButtons.Visibility = Visibility.Visible;

            txtSecret.IsEnabled = true;
            secretCopiedTimer.Stop();
            txtSecret.Background = null;
            btnCopySecret.Visibility = Visibility.Collapsed;
            txtDuration.IsEnabled = true;
            txtLength.IsEnabled = true;
            cmbAlgo.IsEnabled = true;
            btnAddAccountManual.Visibility = Visibility.Visible;
            btnEditAccount.Visibility = Visibility.Collapsed;

            EditMode = false;
        }

        private void btnBack_Click(object sender, RoutedEventArgs e)
        {
            lbCodes.Visibility = Visibility.Visible;
            pnlBottom.Visibility = Visibility.Visible;
            grdAdd.Visibility = Visibility.Collapsed;
            grdManual.Visibility = Visibility.Collapsed;
        }

        private void btnAddManual_Click(object sender, RoutedEventArgs e)
        {
            if (grdManual.Visibility == Visibility.Collapsed) {
                grdManual.Visibility = Visibility.Visible;

                txtName.Text = string.Empty;
                txtIssuer.Text = string.Empty;
                txtSecret.Text = string.Empty;
                txtDuration.Text = "30";
                txtLength.Text = "6";
                cmbAlgo.SelectedIndex = 0;

                txtName.Focus();
            }
        }

        private void lblAdvance_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (grdAdvance.Visibility == Visibility.Collapsed)
            {
                grdAdvance.Visibility = Visibility.Visible;
                lblAdvance.Content = "\u2BC6 Advance";
            }
            else
            {
                grdAdvance.Visibility = Visibility.Collapsed;
                lblAdvance.Content = "\u2BC8 Advance";
            }

        }

        private void btnAddAccountManual_Click(object sender, RoutedEventArgs e)
        {
            bool OK = true;
            Account account = new Account();

            if (string.IsNullOrEmpty(txtName.Text))
            {
                txtName.Background = Brushes.LightCoral;
                OK = false;
            }
            else
            {
                account.Name = txtName.Text.Trim();
                txtName.Background = null;
            }

            /*
            if (string.IsNullOrEmpty(txtIssuer.Text))
            {
                txtIssuer.Background = Brushes.LightCoral;
                OK = false;
            }
            else
            {
                account.Issuer = txtIssuer.Text.Trim();
                txtIssuer.Background = null;
            }*/
            account.Issuer = txtIssuer.Text.Trim();

            if (string.IsNullOrEmpty(txtSecret.Text))
            {
                txtSecret.Background = Brushes.LightCoral;
                OK = false;
            }
            else
            {
                account.Secret = txtSecret.Text.Trim().Replace(" ","");
                txtSecret.Background = null;
            }

            if (grdAdvance.Visibility == Visibility.Visible)
            {
                if (!IsNum(txtDuration.Text))
                {
                    txtDuration.Text = "30";
                }
                if (!IsNum(txtLength.Text))
                {
                    txtLength.Text = "6";
                }
                account.Duration = Int32.Parse(txtDuration.Text);
                account.Length = Int32.Parse(txtLength.Text);
                account.HashAlgo = (Account.Hash)Enum.ToObject(typeof(Account.Hash), cmbAlgo.SelectedIndex);
            }
            else
            {
                account.Duration = 30;
                account.Length = 6;
                account.HashAlgo = Account.Hash.SHA1;
            }

            if (OK)
            {
                GenerateCode(account);
                account.Id = accounts.Count;
                accounts.Add(account);
                ApplySort();
                WriteJson();

                txtName.Text = "";
                txtIssuer.Text = "";
                txtSecret.Text = "";
                txtDuration.Text = "30";
                cmbAlgo.SelectedIndex = 0;
                txtLength.Text = "6";
                lbCodes.Visibility = Visibility.Visible;
                pnlBottom.Visibility = Visibility.Visible;
                grdAdd.Visibility = Visibility.Collapsed;
                grdManual.Visibility = Visibility.Collapsed;
            }
        }

        private bool IsNum(string txt)
        {
            var isNumeric = int.TryParse(txt, out int n);
            if (isNumeric)
            {
                if (n < 1)
                    return false;
                else
                    return true;
            }
            else
                return false;
        }

        private void GenerateCode(Account acc)
        {
            var base32Bytes = Base32Encoding.ToBytes(acc.Secret);
            var totp = new Totp(base32Bytes, acc.Duration, (OtpHashMode)Enum.ToObject(typeof(OtpHashMode), acc.HashAlgo), acc.Length);
            var totpCode = totp.ComputeTotp();
            acc.Totp = totpCode.ToString().Insert(totpCode.ToString().Length / 2, " ");
            acc.Seconds = totp.RemainingSeconds();
        }

        private string GetUri(Account acc)
        {
            string Uri = new OtpUri(OtpType.Totp, acc.Secret, acc.Name, acc.Issuer, (OtpHashMode)Enum.ToObject(typeof(OtpHashMode), acc.HashAlgo), acc.Length, acc.Duration).ToString();
            Uri = Uri.Replace("&algorithm=SHA1", "").Replace("&digits=6", "").Replace("&period=30", "");
            return Uri;
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            foreach (Account acc in accounts)
            {
                if (acc.Seconds > 1)
                    acc.Seconds--;
                else
                    GenerateCode(acc);
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            lblCopy.Content = "Click on a code to copy";
            lblCopy.Foreground = Brushes.Gray;
            lblCopy.Background = null;
            timer2.Stop();
        }

        private void WriteJson()
        {
            var opt = new JsonSerializerOptions() { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(accounts, opt);
            VaultSession.Save(AccounsFile, vaultSession, jsonString);
        }

        private bool InitializeVault()
        {
            if (!File.Exists(AccounsFile))
            {
                var dialog = new VaultPasswordWindow(true);
                if (dialog.ShowDialog() != true) return false;
                string password = dialog.Password;
                VaultSession session = VaultWorkWindow.Run("Creating your account file...", () =>
                {
                    VaultSession created = VaultSession.Create(password);
                    try
                    {
                        VaultSession.Save(AccounsFile, created, JsonSerializer.Serialize(accounts));
                        return created;
                    }
                    catch
                    {
                        created.Dispose();
                        throw;
                    }
                });
                vaultSession = session;
                return true;
            }

            string contents = File.ReadAllText(AccounsFile);
            if (contents.TrimStart().StartsWith("{"))
            {
                string unlockError = null;
                while (true)
                {
                    var dialog = new VaultPasswordWindow(false, false, unlockError);
                    if (dialog.ShowDialog() != true) return false;
                    string password = dialog.Password;
                    var opened = VaultWorkWindow.Run("Unlocking your accounts...", () =>
                    {
                        string json;
                        VaultSession session;
                        bool matched = VaultSession.TryUnlock(contents, password, out session, out json);
                        return Tuple.Create(matched, session, json);
                    });
                    if (!opened.Item1)
                    {
                        unlockError = "Incorrect password or damaged account file. Please try again.";
                        continue;
                    }
                    var loaded = JsonSerializer.Deserialize<ObservableCollection<Account>>(opened.Item3);
                    if (loaded == null) throw new InvalidDataException("The account data is invalid.");
                    accounts = loaded;
                    vaultSession = opened.Item2;
                    try
                    {
                        VaultSession.RemoveCompletedMigrationBackups(AccounsFile);
                    }
                    catch (IOException)
                    {
                        MessageBox.Show("A legacy migration backup could not be removed. Please check the AuthWin folder in AppData.",
                            "AuthWin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        MessageBox.Show("A legacy migration backup could not be removed. Please check the AuthWin folder in AppData.",
                            "AuthWin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;
                }
            }
            else
            {
                // This key is used only to read files created by older AuthWin versions.
                const string legacyKey = "0297D45A92EC5382428A1E387FEDC12DE0BBD0DA54E20D9E37D64CD170A5BFC1";
                string json = EncDec.Decrypt(contents, legacyKey).Trim('\0');
                var loaded = JsonSerializer.Deserialize<ObservableCollection<Account>>(json);
                if (loaded == null) throw new InvalidDataException("The legacy account data is invalid.");

                var dialog = new VaultPasswordWindow(true, true);
                if (dialog.ShowDialog() != true) return false;
                string password = dialog.Password;
                VaultSession session = VaultWorkWindow.Run("Protecting your existing accounts...", () =>
                {
                    VaultSession created = VaultSession.Create(password);
                    try
                    {
                        VaultSession.Save(AccounsFile, created, json, true);
                        return created;
                    }
                    catch
                    {
                        created.Dispose();
                        throw;
                    }
                });
                accounts = loaded;
                vaultSession = session;
            }

            for (int i = 0; i < accounts.Count; i++) accounts[i].Id = i;
            return true;
        }

        private void lbCodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Account selected = lbCodes.SelectedItem as Account;
            if (selected != null)
            {
                Clipboard.SetText(selected.Totp.Replace(" ", ""));
                lblCopy.Content = "Code copied to clipboard";
                lblCopy.Foreground = Brushes.Blue;
                lblCopy.Background = Brushes.Yellow;
                timer2.Stop();
                timer2.Start();
                lbCodes.SelectedIndex = -1;
            }
        }

        private void QR_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            int i = (int)((System.Windows.Controls.Image)sender).Tag;

            string Uri = GetUri(accounts[i]);
            txtUri.Text = Uri;

            QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(Uri, QRCodeGenerator.ECCLevel.Q);
            QRCode qrCode = new QRCode(qrCodeData);
            qrCodeImage = qrCode.GetGraphic(10);

            using (var ms = new MemoryStream())
            {
                qrCodeImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();

                imgQR.Source = bi;
            }

            lbCodes.Visibility = Visibility.Collapsed;
            grdQR.Visibility = Visibility.Visible;
            lblCopy.Visibility = Visibility.Hidden;
            pnlBottom.Visibility = Visibility.Hidden;

            e.Handled = true;
        }

        private void Edit_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            e.Handled = true;
            int i = (int)((Image)sender).Tag;
            EditIndex = i;

            lbCodes.Visibility = Visibility.Collapsed;
            pnlBottom.Visibility = Visibility.Collapsed;
            grdAdd.Visibility = Visibility.Visible;
            grdAddButtons.Visibility = Visibility.Hidden;

            EditMode = true;

            txtName.Text = accounts[i].Name;
            txtIssuer.Text = accounts[i].Issuer;
            txtSecret.Text = accounts[i].Secret;
            secretCopiedTimer.Stop();
            txtSecret.Background = null;
            txtDuration.Text = accounts[i].Duration.ToString();
            txtLength.Text = accounts[i].Length.ToString();
            cmbAlgo.SelectedIndex = (int)accounts[i].HashAlgo;

            txtSecret.IsEnabled = false;
            btnCopySecret.Visibility = Visibility.Visible;
            txtDuration.IsEnabled = false;
            txtLength.IsEnabled = false;
            cmbAlgo.IsEnabled = false;
            btnAddAccountManual.Visibility = Visibility.Collapsed;
            btnEditAccount.Visibility = Visibility.Visible;

            grdManual.Visibility = Visibility.Visible;
        }

        private void btnCopySecret_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtSecret.Text);
            txtSecret.Background = Brushes.LightGreen;
            secretCopiedTimer.Stop();
            secretCopiedTimer.Start();
        }

        private void secretCopiedTimer_Tick(object sender, EventArgs e)
        {
            secretCopiedTimer.Stop();
            txtSecret.Background = null;
        }

        private void Delete_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            int i = (int)((Image)sender).Tag;
            string Name = accounts[i].Issuer + " " + accounts[i].Name;
            if (MessageBox.Show(String.Format("Are you sure you want to delete {0}?", Name), "AuthWin", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                accounts.RemoveAt(i);
            }
            for (int n = 0; n < accounts.Count; n++)
            {
                accounts[n].Id = n;
            }
            lbCodes.Items.Refresh();
            WriteJson();
            e.Handled = true;
        }

        private void Image_MouseEnter(object sender, MouseEventArgs e)
        {
            ((Image)sender).Opacity = 1;
        }

        private void Image_MouseLeave(object sender, MouseEventArgs e)
        {
            ((Image)sender).Opacity = 0.4;
        }

        private void btnCloseQR_Click(object sender, RoutedEventArgs e)
        {
            grdQR.Visibility = Visibility.Collapsed;
            lbCodes.Visibility = Visibility.Visible;
            lblCopy.Visibility = Visibility.Visible;
            pnlBottom.Visibility = Visibility.Visible;
            btnCopyUri.Content = "Copy";
        }

        private void btnCopyUri_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtUri.Text);
            btnCopyUri.Content = "Copied";
        }

        private void btnSaveQR_Click(object sender, RoutedEventArgs e)
        {
            if (qrCodeImage != null)
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.AddExtension = true;
                sfd.Filter = "PNG Image|*.png";
                sfd.FileName = "QRCode.png";
                if (sfd.ShowDialog() == true)
                {
                    qrCodeImage.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                }
                sfd = null;
            }
        }

        private void btnScanQR_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Images|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.gif";
            if (ofd.ShowDialog() == true)
            {
                IBarcodeReader reader = new BarcodeReader();
                var barcodeBitmap = (System.Drawing.Bitmap)System.Drawing.Image.FromFile(ofd.FileName);
                var result = reader.Decode(barcodeBitmap);
                if (result != null)
                {
                    string Uri = result.Text;
                    if (Uri.StartsWith("otpauth://totp/"))
                    {
                        Account acc = new Account();

                        Uri = Uri.Replace("otpauth://totp/", "");
                        string ni = Uri.Substring(0, Uri.IndexOf("?"));
                        ni = System.Web.HttpUtility.UrlDecode(ni);
                        acc.Issuer = ni.Split(':')[0];
                        acc.Name = ni.Split(':')[1];

                        string qs = Uri.Substring(Uri.IndexOf("?") + 1);
                        string[] qsparams = qs.Split('&');
                        foreach (string qsparam in qsparams)
                        {
                            string[] parts = qsparam.Split('=');
                            switch (parts[0].ToLower())
                            {
                                case "secret":
                                    acc.Secret = parts[1];
                                    break;
                                case "digits":
                                    acc.Length = Convert.ToInt32(parts[1]);
                                    break;
                                case "period":
                                    acc.Duration = Convert.ToInt32(parts[1]);
                                    break;
                                case "algorithm":
                                    acc.HashAlgo = (Account.Hash)Enum.Parse(typeof(Account.Hash), parts[1], true);
                                    break;
                            }
                        }

                        acc.Id = accounts.Count;
                        GenerateCode(acc);
                        accounts.Add(acc);
                        ApplySort();
                        WriteJson();

                        lbCodes.Visibility = Visibility.Visible;
                        pnlBottom.Visibility = Visibility.Visible;
                        grdAdd.Visibility = Visibility.Collapsed;
                        grdManual.Visibility = Visibility.Collapsed;
                    }
                    else if (Uri.StartsWith("otpauth-migration:")) {
                        try
                        {
                            Uri = Uri.Replace("otpauth-migration://offline?data=", "");
                            Uri = HttpUtility.UrlDecode(Uri);
                            byte[] bytes = System.Convert.FromBase64String(Uri);
                            MigrationPayload payload = new MigrationPayload();
                            payload = MigrationPayload.Parser.ParseFrom(bytes);

                            if (payload.OtpParameters.Count > 0) {
                                for (int i = 0; i < payload.OtpParameters.Count; i++)
                                {
                                    Account acc = new Account();
                                    acc.Name = payload.OtpParameters[i].Name;
                                    acc.Issuer = payload.OtpParameters[i].Issuer;
                                    acc.Secret = Base32.ToBase32String(payload.OtpParameters[i].Secret.ToByteArray());
                                    if (payload.OtpParameters[i].Digits == 2) acc.Length = 8;

                                    acc.Id = accounts.Count;
                                    GenerateCode(acc);
                                    accounts.Add(acc);
                                }
                                ApplySort();
                                WriteJson();
                            }
                        }
                        catch {
                            MessageBox.Show("Error reading Google Authenticator code", "AuthWin", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                        }
                        finally {
                            lbCodes.Visibility = Visibility.Visible;
                            pnlBottom.Visibility = Visibility.Visible;
                            grdAdd.Visibility = Visibility.Collapsed;
                            grdManual.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Not a valid QR code", "AuthWin", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                }
                else
                {
                    MessageBox.Show("Cannot read the QR code!", "AuthWin", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

        }

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            pnlImport.Visibility = Visibility.Collapsed;
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.FileName = "Accounts.json";
            sfd.AddExtension = true;
            sfd.Filter = "JSON File|*.json";
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    if (!string.Equals(System.IO.Path.GetFullPath(sfd.FileName),
                        System.IO.Path.GetFullPath(AccounsFile), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(AccounsFile, sfd.FileName, true);
                    }
                    MessageBox.Show("Encrypted accounts exported. Keep the vault password safe!", "AuthWin",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not export accounts.\n\n" + ex.Message, "AuthWin",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnImport_Click(object sender, RoutedEventArgs e)
        {
            if (pnlImport.Visibility == Visibility.Collapsed)
            {
                pnlImport.Visibility = Visibility.Visible;
            }
            else {
                pnlImport.Visibility = Visibility.Collapsed;
            }
        }

        private void btnEditAccount_Click(object sender, RoutedEventArgs e)
        {
            if (EditMode) {
                if (string.IsNullOrEmpty(txtName.Text))
                {
                    txtName.Background = Brushes.LightCoral;
                    return;
                }
                else
                {
                    accounts[EditIndex].Name = txtName.Text.Trim();
                    txtName.Background = null;
                }

                accounts[EditIndex].Issuer = txtIssuer.Text.Trim();
                ApplySort();
                lbCodes.Items.Refresh();
                WriteJson();

                lbCodes.Visibility = Visibility.Visible;
                pnlBottom.Visibility = Visibility.Visible;
                grdAdd.Visibility = Visibility.Collapsed;
                grdManual.Visibility = Visibility.Collapsed;
                grdAddButtons.Visibility = Visibility.Visible;
                btnEditAccount.Visibility = Visibility.Collapsed;
                btnAddAccountManual.Visibility = Visibility.Visible;
                EditMode = false;
            }
        }

        private void lblWeb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Process.Start("https://authwin.com");
        }

        private void btnImportFinal_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "JSON File|*.json";
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string fileContents = File.ReadAllText(ofd.FileName);
                    string jsonString;
                    if (fileContents.TrimStart().StartsWith("{"))
                    {
                        string importError = null;
                        while (true)
                        {
                            var passwordDialog = new VaultPasswordWindow(false, false, importError,
                                "Unlock Imported Accounts", "Enter the password for the file you are importing.");
                            if (passwordDialog.ShowDialog() != true)
                            {
                                pnlImport.Visibility = Visibility.Collapsed;
                                return;
                            }
                            string password = passwordDialog.Password;
                            var opened = VaultWorkWindow.Run("Unlocking imported accounts...", () =>
                            {
                                VaultSession importedSession;
                                string decrypted;
                                bool matched = VaultSession.TryUnlock(fileContents, password, out importedSession, out decrypted);
                                importedSession?.Dispose();
                                return Tuple.Create(matched, decrypted);
                            });
                            if (opened.Item1)
                            {
                                jsonString = opened.Item2;
                                break;
                            }
                            importError = "Incorrect password or damaged account file. Please try again.";
                        }
                    }
                    else if (chkImportPass.IsChecked == true)
                    {
                        if (string.IsNullOrEmpty(txtImportPass.Text))
                            throw new InvalidDataException("Enter the password for the old export file.");
                        string passHash = EncDec.Sha256(txtImportPass.Text.Trim());
                        jsonString = EncDec.Decrypt(fileContents, passHash);
                    }
                    else
                        jsonString = fileContents;

                    var imported = JsonSerializer.Deserialize<ObservableCollection<Account>>(jsonString);
                    if (imported == null) throw new InvalidDataException("The imported account data is invalid.");
                    for (int i = 0; i < imported.Count; i++)
                    {
                        imported[i].Id = accounts.Count + i;
                        GenerateCode(imported[i]);
                    }

                    var merged = new List<Account>(accounts);
                    merged.AddRange(imported);
                    string mergedJson = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
                    VaultSession.Save(AccounsFile, vaultSession, mergedJson);
                    foreach (Account account in imported) accounts.Add(account);
                    ApplySort();
                    MessageBox.Show("Accounts imported into the current vault.", "AuthWin",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not import accounts.\n\n" + ex.Message, "AuthWin",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            ofd = null;
            pnlImport.Visibility = Visibility.Collapsed;
        }

        private void chkImportPass_Checked(object sender, RoutedEventArgs e)
        {
            txtImportPass.Visibility = Visibility.Visible;
            txtImportPass.Focus();
        }

        private void chkImportPass_Unchecked(object sender, RoutedEventArgs e)
        {
            txtImportPass.Visibility = Visibility.Hidden;
        }

        private void WriteLog(string message)
        {
            string logMessage = $"{DateTime.Now}: {message}";
            System.IO.File.AppendAllText(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\AuthWin\\log.txt", logMessage + Environment.NewLine);
        }

        private void cmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!sortUiReady) return;
            sortMode = IndexToSortMode(cmbSort.SelectedIndex);
            SaveSortPreference();
            ApplySort();
        }

        private void ApplySort()
        {
            ListCollectionView view = CollectionViewSource.GetDefaultView(accounts) as ListCollectionView;
            if (view == null) return;
            view.CustomSort = sortMode == AccountSortMode.Added ? null : new AccountDisplayComparer(sortMode);
        }

        private void LoadSortPreference()
        {
            try
            {
                if (File.Exists(PrefsFile))
                {
                    UiPreferences prefs = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(PrefsFile));
                    if (prefs != null && Enum.TryParse(prefs.SortBy, true, out AccountSortMode loaded))
                        sortMode = loaded;
                }
            }
            catch
            {
                sortMode = AccountSortMode.Added;
            }

            cmbSort.SelectedIndex = SortModeToIndex(sortMode);
        }

        private void SaveSortPreference()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefsFile));
                var prefs = new UiPreferences { SortBy = sortMode.ToString() };
                File.WriteAllText(PrefsFile, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        private static AccountSortMode IndexToSortMode(int index)
        {
            switch (index)
            {
                case 1: return AccountSortMode.Issuer;
                case 2: return AccountSortMode.Name;
                default: return AccountSortMode.Added;
            }
        }

        private static int SortModeToIndex(AccountSortMode mode)
        {
            switch (mode)
            {
                case AccountSortMode.Issuer: return 1;
                case AccountSortMode.Name: return 2;
                default: return 0;
            }
        }

    }

    internal enum AccountSortMode
    {
        Added,
        Issuer,
        Name
    }

    internal sealed class UiPreferences
    {
        public string SortBy { get; set; }
    }

    internal sealed class AccountDisplayComparer : IComparer
    {
        private readonly AccountSortMode sortMode;

        public AccountDisplayComparer(AccountSortMode sortMode)
        {
            this.sortMode = sortMode;
        }

        public int Compare(object x, object y)
        {
            Account left = x as Account;
            Account right = y as Account;
            if (left == null && right == null) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            int result;
            if (sortMode == AccountSortMode.Issuer)
            {
                result = string.Compare(left.Issuer, right.Issuer, StringComparison.CurrentCultureIgnoreCase);
                if (result != 0) return result;
                result = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            }
            else
            {
                result = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
                if (result != 0) return result;
                result = string.Compare(left.Issuer, right.Issuer, StringComparison.CurrentCultureIgnoreCase);
            }

            if (result != 0) return result;
            return left.Id.CompareTo(right.Id);
        }
    }

    public class Account : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Issuer { get; set; }
        public string Secret { get; set; }
        public int Duration { get; set; }
        public int Length { get; set; }
        public Hash HashAlgo { get; set; }

        private string _totp;
        [JsonIgnore]
        public string Totp
        {
            get => _totp;
            set
            {
                if (_totp != value)
                {
                    _totp = value;
                    OnPropertyChanged(nameof(Totp));
                }
            }
        }
        [JsonIgnore]
        private int _seconds;
        [JsonIgnore]
        public int Seconds
        {
            get => _seconds;
            set
            {
                if (_seconds != value)
                {
                    _seconds = value;
                    OnPropertyChanged(nameof(Seconds));
                    OnPropertyChanged(nameof(PieGeometry));
                }
            }
        }
        [JsonIgnore] public int Id { get; set; }

        public enum Hash
        {
            SHA1,
            SHA256,
            SHA512
        }

        public Account()
        {
            Duration = 30;
            Length = 6;
            HashAlgo = Hash.SHA1;
        }

        [JsonIgnore]
        public Geometry PieGeometry
        {
            get
            {
                double percent = (double)Seconds / (Duration > 0 ? Duration : 30);
                double angle = 360 * percent;
                return CreatePieGeometry(16, 16, 15, angle);
            }
        }

        private Geometry CreatePieGeometry(double cx, double cy, double radius, double angle)
        {
            if (angle <= 0) return Geometry.Empty;
            if (angle >= 360) angle = 359.999;

            double radians = (Math.PI / 180) * (angle - 90);
            double x = cx + radius * Math.Cos(radians);
            double y = cy + radius * Math.Sin(radians);

            bool isLargeArc = angle > 180;

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new System.Windows.Point(cx, cy), true, true);
                ctx.LineTo(new System.Windows.Point(cx, cy - radius), true, false);
                ctx.ArcTo(new System.Windows.Point(x, y), new System.Windows.Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            }
            geom.Freeze();
            return geom;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
