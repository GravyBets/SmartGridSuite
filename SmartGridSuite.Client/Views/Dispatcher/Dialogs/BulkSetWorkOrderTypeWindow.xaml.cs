using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class BulkSetWorkOrderTypeWindow : Window
    {
        public string WorkOrderType
        {
            get
            {
                var selected =
                    WorkOrderTypeComboBox.SelectedItem
                        ?.ToString()
                        ?.Trim()
                    ?? "";

                return selected.Equals(
                    "Blank / Clear WO Type",
                    StringComparison.OrdinalIgnoreCase)
                        ? ""
                        : selected;
            }
        }

        public BulkSetWorkOrderTypeWindow(int ticketCount)
        {
            InitializeComponent();

            HeaderTextBlock.Text =
                $"Set WO Type for {ticketCount} selected ticket(s)";

            WorkOrderTypeComboBox.ItemsSource = new[]
                {
                    "Blank / Clear WO Type",
                    "Maintenance",
                    "Capital",
                    "Distribution"
                };

            WorkOrderTypeComboBox.SelectedIndex = 0;

            Loaded += (_, _) =>
            {
                WorkOrderTypeComboBox.Focus();
                WorkOrderTypeComboBox.IsDropDownOpen = true;
            };
        }

        private void Apply_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (WorkOrderTypeComboBox.SelectedItem == null)
            {
                MessageBox.Show(
                    "Choose a Work Order Type.",
                    "Set WO Type",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                WorkOrderTypeComboBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}