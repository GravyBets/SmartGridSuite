using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class ConfirmDeleteTicketWindow : Window
    {
        public ConfirmDeleteTicketWindow(
            string? site,
            string? notification,
            string? problem)
        {
            InitializeComponent();

            SiteTextBlock.Text =
                string.IsNullOrWhiteSpace(site)
                    ? "(No Site)"
                    : site.Trim();

            NotificationTextBlock.Text =
                string.IsNullOrWhiteSpace(notification)
                    ? "(No Notification)"
                    : notification.Trim();

            ProblemTextBlock.Text =
                string.IsNullOrWhiteSpace(problem)
                    ? "(No Problem / Issue)"
                    : problem.Trim();
        }

        private void ConfirmDeleteButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}