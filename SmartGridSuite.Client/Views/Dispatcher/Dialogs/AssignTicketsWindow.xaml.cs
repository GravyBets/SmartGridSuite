using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class AssignTicketsWindow : Window
    {
        public string AssignedTech =>
            TechnicianComboBox.SelectedItem?.ToString()?.Trim() ?? "";

        public AssignTicketsWindow(int ticketCount, IEnumerable<string> techSuggestions)
        {
            InitializeComponent();

            HeaderTextBlock.Text = $"Assign {ticketCount} selected ticket(s)";

            var items = techSuggestions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x)
                .ToList();

            TechnicianComboBox.ItemsSource = items;

            if (items.Count > 0)
                TechnicianComboBox.SelectedIndex = 0;

            Loaded += (_, _) =>
            {
                TechnicianComboBox.Focus();
                TechnicianComboBox.IsDropDownOpen = true;
            };
        }

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AssignedTech))
            {
                MessageBox.Show(
                    "Choose a technician or (Unassigned).",
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                TechnicianComboBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}