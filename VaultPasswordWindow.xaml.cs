using System.Windows;

namespace AuthWin
{
    public sealed partial class VaultPasswordWindow : Window
    {
        private readonly bool creating;

        internal string Password { get; private set; }

        internal VaultPasswordWindow(bool creating, bool migrating = false, string errorMessage = null,
            string title = null, string instructions = null)
        {
            InitializeComponent();
            this.creating = creating;
            Title = creating ? "Create AuthWin Password" : "Unlock AuthWin";
            txtInstructions.Text = migrating
                ? "Your existing accounts will be protected with this password. Keep it safe; it cannot be recovered."
                : creating
                    ? "Create a password for your accounts. Keep it safe; it cannot be recovered."
                    : "Enter your password to unlock your accounts.";
            if (title != null) Title = title;
            if (instructions != null) txtInstructions.Text = instructions;
            pnlConfirmation.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
            btnSubmit.Content = creating ? "Create" : "Unlock";
            if (!string.IsNullOrEmpty(errorMessage))
            {
                txtError.Text = errorMessage;
                txtError.Visibility = Visibility.Visible;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            passwordBox.Focus();
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            string password = passwordBox.Password;
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show(this, "Enter a password.", "AuthWin", MessageBoxButton.OK, MessageBoxImage.Warning);
                passwordBox.Focus();
                return;
            }
            if (creating && password != confirmationBox.Password)
            {
                MessageBox.Show(this, "Passwords do not match.", "AuthWin", MessageBoxButton.OK, MessageBoxImage.Warning);
                confirmationBox.Clear();
                confirmationBox.Focus();
                return;
            }

            Password = password;
            passwordBox.Clear();
            confirmationBox.Clear();
            DialogResult = true;
        }
    }
}
