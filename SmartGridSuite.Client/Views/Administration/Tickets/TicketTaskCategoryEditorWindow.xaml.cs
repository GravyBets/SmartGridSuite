using SmartGridSuite.Contracts.Administration;
using SmartGridSuite.Contracts.Administration.Ticket.Status;
using SmartGridSuite.Contracts.Administration.Ticket;
using System.Windows;

namespace SmartGridSuite.Client.Views.Administration.Tickets
{
    public partial class TicketTaskCategoryEditorWindow : Window
    {
        public string CategoryName => NameTextBox.Text.Trim();
        public string DefaultActionRequired => DefaultActionRequiredTextBox.Text.Trim();
        public int SortOrder { get; private set; }
        public bool CategoryIsActive => IsActiveCheckBox.IsChecked == true;

        public TicketTaskCategoryEditorWindow()
        {
            InitializeComponent();
            Title = "Add Task Category";
        }

        public TicketTaskCategoryEditorWindow(TicketTaskCategoryDto existing)
        {
            InitializeComponent();

            Title = "Edit Task Category";

            NameTextBox.Text = existing.Name;
            DefaultActionRequiredTextBox.Text = existing.DefaultActionRequired;
            SortOrderTextBox.Text = existing.SortOrder.ToString();
            IsActiveCheckBox.IsChecked = existing.IsActive;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CategoryName))
            {
                MessageBox.Show(
                    "Category name is required.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(DefaultActionRequired))
            {
                MessageBox.Show(
                    "Default Action Required is required.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DefaultActionRequiredTextBox.Focus();
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