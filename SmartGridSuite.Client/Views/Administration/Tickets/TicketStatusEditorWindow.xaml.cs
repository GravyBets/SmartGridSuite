using System.Windows;
using SmartGridSuite.Contracts.Administration.Ticket.Status;

namespace SmartGridSuite.Client.Views.Administration.Tickets
{
    public partial class TicketStatusEditorWindow : Window
    {
        public string StatusName => NameTextBox.Text.Trim();
        public int SortOrder { get; private set; }
        public bool StatusIsActive => IsActiveCheckBox.IsChecked == true;
        public bool IsClosed => IsClosedCheckBox.IsChecked == true;
        public bool ShowInFilter => ShowInFilterCheckBox.IsChecked == true;
        public bool SendToDispatchTasks => SendToDispatchTasksCheckBox.IsChecked == true;

        public TicketStatusEditorWindow()
        {
            InitializeComponent();
            Title = "Add Ticket Status";
        }

        public TicketStatusEditorWindow(TicketStatusDto existing)
        {
            InitializeComponent();

            Title = "Edit Ticket Status";

            NameTextBox.Text = existing.Name;
            SortOrderTextBox.Text = existing.SortOrder.ToString();
            IsActiveCheckBox.IsChecked = existing.IsActive;
            IsClosedCheckBox.IsChecked = existing.IsClosed;
            ShowInFilterCheckBox.IsChecked = existing.ShowInFilter;
            SendToDispatchTasksCheckBox.IsChecked = existing.SendToDispatchTasks;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StatusName))
            {
                MessageBox.Show(
                    "Status name is required.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            if (!int.TryParse(SortOrderTextBox.Text.Trim(), out var sortOrder))
            {
                MessageBox.Show(
                    "Sort Order must be a whole number.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                SortOrderTextBox.Focus();
                return;
            }

            SortOrder = sortOrder;
            DialogResult = true;
        }
    }
}