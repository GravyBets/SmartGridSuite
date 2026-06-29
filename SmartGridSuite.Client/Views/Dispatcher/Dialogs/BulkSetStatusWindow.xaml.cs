using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class BulkSetStatusWindow : Window
    {
        public string SelectedStatus =>
            StatusComboBox.SelectedItem?.ToString()?.Trim() ?? "";

        public BulkSetStatusWindow(int ticketCount, IEnumerable<string> statuses)
        {
            InitializeComponent();

            HeaderTextBlock.Text =
                $"Set Status for {ticketCount} selected ticket(s)";

            var items = statuses
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            StatusComboBox.ItemsSource = items;

            StatusComboBox.SelectedIndex = -1;

            Loaded += (_, _) =>
            {
                StatusComboBox.Focus();
                StatusComboBox.IsDropDownOpen = true;
            };
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedStatus))
            {
                MessageBox.Show(
                    "Choose a status.",
                    "Set Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                StatusComboBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}