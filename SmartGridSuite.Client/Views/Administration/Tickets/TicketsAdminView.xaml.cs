using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration;
using SmartGridSuite.Contracts.Administration.Ticket.Status;

namespace SmartGridSuite.Client.Views.Administration.Tickets
{
    public partial class TicketsAdminView : UserControl
    {
        private readonly TicketAdminApi _ticketAdminApi;
        private bool _hasLoadedOnce;
        private bool _isLoading;

        private TicketStatusDto? SelectedStatus => StatusesGrid.SelectedItem as TicketStatusDto;
        private TicketTaskCategoryDto? SelectedCategory => CategoriesGrid.SelectedItem as TicketTaskCategoryDto;

        public ObservableCollection<TicketStatusDto> Statuses { get; } = new();
        public ObservableCollection<TicketTaskCategoryDto> Categories { get; } = new();

        public TicketsAdminView(ApiClient api)
        {
            InitializeComponent();

            _ticketAdminApi = new TicketAdminApi(api);

            StatusesGrid.ItemsSource = Statuses;
            CategoriesGrid.ItemsSource = Categories;

            UpdateStatusButtons();
            UpdateCategoryButtons();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedOnce)
                return;

            _hasLoadedOnce = true;
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_isLoading)
                return;

            _isLoading = true;

            try
            {
                var statuses = await _ticketAdminApi.GetStatusesAsync();
                var categories = await _ticketAdminApi.GetTaskCategoriesAsync();

                Statuses.Clear();
                foreach (var item in statuses)
                    Statuses.Add(item);

                Categories.Clear();
                foreach (var item in categories)
                    Categories.Add(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load ticket administration data.\n\n{ex.Message}",
                    "Tickets Admin Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
            UpdateStatusButtons();
            UpdateCategoryButtons();
        }

        private void StatusesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusButtons();
        }

        private void UpdateStatusButtons()
        {
            var hasSelection = SelectedStatus != null;
            var isSystemRequired = IsSystemRequiredStatus(SelectedStatus?.Name);

            EditStatusButton.IsEnabled = hasSelection;
            DeactivateStatusButton.IsEnabled =
                hasSelection &&
                SelectedStatus?.IsActive == true &&
                !isSystemRequired;
        }

        private async void AddStatus_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TicketStatusEditorWindow
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();
            if (result != true)
                return;

            try
            {
                await _ticketAdminApi.CreateStatusAsync(new CreateTicketStatusRequest
                {
                    Name = dialog.StatusName,
                    SortOrder = dialog.SortOrder,
                    IsActive = dialog.StatusIsActive,
                    IsClosed = dialog.IsClosed,
                    ShowInFilter = dialog.ShowInFilter,
                    SendToDispatchTasks = dialog.SendToDispatchTasks
                });

                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create ticket status.\n\n{ex.Message}",
                    "Create Status Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void EditStatus_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedStatus == null)
                return;

            var dialog = new TicketStatusEditorWindow(SelectedStatus)
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();
            if (result != true)
                return;

            try
            {
                await _ticketAdminApi.UpdateStatusAsync(new UpdateTicketStatusRequest
                {
                    Id = SelectedStatus.Id,
                    Name = dialog.StatusName,
                    SortOrder = dialog.SortOrder,
                    IsActive = dialog.StatusIsActive,
                    IsClosed = dialog.IsClosed,
                    ShowInFilter = dialog.ShowInFilter,
                    SendToDispatchTasks = dialog.SendToDispatchTasks
                });

                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update ticket status.\n\n{ex.Message}",
                    "Update Status Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void DeactivateStatus_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedStatus == null)
                return;

            if (IsSystemRequiredStatus(SelectedStatus.Name))
            {
                MessageBox.Show(
                    GetSystemRequiredStatusMessage(SelectedStatus.Name),
                    "Protected Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Deactivate '{SelectedStatus.Name}'?",
                "Deactivate Status",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await _ticketAdminApi.DeactivateStatusAsync(SelectedStatus.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to deactivate ticket status.\n\n{ex.Message}",
                    "Deactivate Status Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CategoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCategoryButtons();
        }

        private void UpdateCategoryButtons()
        {
            var hasSelection = SelectedCategory != null;

            EditCategoryButton.IsEnabled = hasSelection;
            DeactivateCategoryButton.IsEnabled = hasSelection && SelectedCategory?.IsActive == true;
        }

        private async void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TicketTaskCategoryEditorWindow
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();
            if (result != true)
                return;

            try
            {
                await _ticketAdminApi.CreateTaskCategoryAsync(new CreateTicketTaskCategoryRequest
                {
                    Name = dialog.CategoryName,
                    DefaultActionRequired = dialog.DefaultActionRequired,
                    SortOrder = dialog.SortOrder,
                    IsActive = dialog.CategoryIsActive
                });

                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create task category.\n\n{ex.Message}",
                    "Create Category Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCategory == null)
                return;

            var dialog = new TicketTaskCategoryEditorWindow(SelectedCategory)
            {
                Owner = Window.GetWindow(this)
            };

            var result = dialog.ShowDialog();
            if (result != true)
                return;

            try
            {
                await _ticketAdminApi.UpdateTaskCategoryAsync(new UpdateTicketTaskCategoryRequest
                {
                    Id = SelectedCategory.Id,
                    Name = dialog.CategoryName,
                    DefaultActionRequired = dialog.DefaultActionRequired,
                    SortOrder = dialog.SortOrder,
                    IsActive = dialog.CategoryIsActive
                });

                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update task category.\n\n{ex.Message}",
                    "Update Category Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void DeactivateCategory_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCategory == null)
                return;

            var confirm = MessageBox.Show(
                $"Deactivate '{SelectedCategory.Name}'?",
                "Deactivate Category",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await _ticketAdminApi.DeactivateTaskCategoryAsync(SelectedCategory.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to deactivate task category.\n\n{ex.Message}",
                    "Deactivate Category Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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

        private static string GetSystemRequiredStatusMessage(string statusName)
        {
            return $"'{statusName}' is required by SmartGridSuite and cannot be deactivated.";
        }
    }
}