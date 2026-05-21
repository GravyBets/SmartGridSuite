using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class AssignTicketsWindow : Window
    {
        public string AssignedTech => (TechnicianComboBox.Text ?? "").Trim();

        public AssignTicketsWindow(int ticketCount, IEnumerable<string> techSuggestions)
        {
            InitializeComponent();

            HeaderTextBlock.Text = $"Assign {ticketCount} selected ticket(s)";

            TechnicianComboBox.ItemsSource = techSuggestions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

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
                    "Choose or type a technician name.",
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