using System.Windows;

namespace SmartGridSuite.Client.Views.Administration.SystemHealth
{
    public partial class RestartApiPasswordWindow : Window
    {
        public RestartApiPasswordWindow()
        {
            InitializeComponent();
            ContentRendered += (_, _) => PasswordInput.Focus();
        }
        public string TakePassword()
        {
            var password = PasswordInput.Password;
            PasswordInput.Clear();
            return password;
        }
        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordInput.SecurePassword.Length == 0)
            {
                ValidationText.Text = "Enter the administrator password.";
                return;
            }
            DialogResult = true;
        }
    }
}
