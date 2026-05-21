using System.Windows;
using SmartGridSuite.Contracts.Administration.Ticket.Status;

namespace SmartGridSuite.Client.Views.Administration.Tickets
{
    public partial class TicketStatusEditorWindow : Window
    {
        private readonly bool _isSystemRequiredStatus;
        private readonly string _originalStatusName = "";
        private readonly bool _originalIsClosed;

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

            _originalStatusName = existing.Name ?? "";
            _originalIsClosed = existing.IsClosed;
            _isSystemRequiredStatus = IsSystemRequiredStatus(existing.Name);

            NameTextBox.Text = existing.Name;
            SortOrderTextBox.Text = existing.SortOrder.ToString();
            IsActiveCheckBox.IsChecked = existing.IsActive;
            IsClosedCheckBox.IsChecked = existing.IsClosed;
            ShowInFilterCheckBox.IsChecked = existing.ShowInFilter;
            SendToDispatchTasksCheckBox.IsChecked = existing.SendToDispatchTasks;

            ApplySystemRequiredProtection();
        }

        private static bool IsSystemRequiredStatus(string? statusName)
        {
            var clean = (statusName ?? "").Trim();

            return clean.Equals("Open", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Assigned", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("In Progress", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Waiting Dispatch", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Needs Review", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Closed", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplySystemRequiredProtection()
        {
            if (!_isSystemRequiredStatus)
                return;

            NameTextBox.IsReadOnly = true;
            IsActiveCheckBox.IsEnabled = false;
            IsClosedCheckBox.IsEnabled = false;

            SystemRequiredTextBlock.Visibility = Visibility.Visible;
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

            if (_isSystemRequiredStatus)
            {
                if (!string.Equals(StatusName, _originalStatusName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        $"'{_originalStatusName}' is required by SmartGridSuite and cannot be renamed.",
                        "Protected Status",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!StatusIsActive)
                {
                    MessageBox.Show(
                        $"'{_originalStatusName}' is required by SmartGridSuite and cannot be deactivated.",
                        "Protected Status",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (IsClosed != _originalIsClosed)
                {
                    MessageBox.Show(
                        $"'{_originalStatusName}' has protected closed-status behavior.",
                        "Protected Status",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            SortOrder = sortOrder;
            DialogResult = true;
        }
    }
}