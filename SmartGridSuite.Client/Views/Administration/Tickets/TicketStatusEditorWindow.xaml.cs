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
        public bool IsFieldComplete => IsFieldCompleteCheckBox.IsChecked == true;
        public bool ShowInFilter => ShowInFilterCheckBox.IsChecked == true;
        public bool IncludeInSummary => IncludeInSummaryCheckBox.IsChecked == true;
        public bool SendToDispatchTasks => SendToDispatchTasksCheckBox.IsChecked == true;
        public bool IsWriteUpSubmitTarget => IsWriteUpSubmitTargetCheckBox.IsChecked == true;

        private const int StatusNameMaxLength = 100;

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
            
            IsActiveCheckBox.IsChecked = existing.IsActive;
            IsClosedCheckBox.IsChecked = existing.IsClosed;
            IsFieldCompleteCheckBox.IsChecked = existing.IsFieldComplete;

            ShowInFilterCheckBox.IsChecked = existing.ShowInFilter;
            IncludeInSummaryCheckBox.IsChecked = existing.IncludeInSummary;
            SendToDispatchTasksCheckBox.IsChecked = existing.SendToDispatchTasks;
            IsWriteUpSubmitTargetCheckBox.IsChecked = existing.IsWriteUpSubmitTarget;

            ApplySystemRequiredProtection();
        }

        private static bool IsSystemRequiredStatus(string? statusName)
        {
            var clean = (statusName ?? "").Trim();

            return clean.Equals("Open", StringComparison.OrdinalIgnoreCase)
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

            if (StatusName.Length > StatusNameMaxLength)
            {
                MessageBox.Show(
                    $"Status name must be {StatusNameMaxLength} characters or less.\n\nCurrent length: {StatusName.Length}",
                    "Status Name Too Long",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                NameTextBox.Focus();
                return;
            }

            if (!StatusIsActive && IsWriteUpSubmitTarget)
            {
                MessageBox.Show(
                    "A status must be active before it can be selected as the Write-Up Target.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

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

            DialogResult = true;
        }
    }
}