#nullable enable
using System.Windows;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class DangerConfirmWindow : Window
    {
        public DangerConfirmWindow(
            string title,
            string message,
            string confirmText)
        {
            InitializeComponent();

            Title = title;
            TitleTextBlock.Text = title;
            MessageTextBlock.Text = message;
            ConfirmButton.Content = confirmText;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}