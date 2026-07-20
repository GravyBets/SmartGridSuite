using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.Ticket.Status;

namespace SmartGridSuite.Client.Views.Administration.Tickets
{
    public partial class TicketsAdminView : UserControl
    {
        private readonly TicketAdminApi _ticketAdminApi;
        private bool _hasLoadedOnce;
        private bool _isLoading;

        private TicketStatusDto? SelectedStatus => StatusesGrid.SelectedItem as TicketStatusDto;

        public ObservableCollection<TicketStatusDto> Statuses { get; } = new();

        public TicketsAdminView(ApiClient api)
        {
            InitializeComponent();

            _ticketAdminApi = new TicketAdminApi(api);

            StatusesGrid.ItemsSource = Statuses;

            UpdateStatusButtons();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedOnce)
                return;

            _hasLoadedOnce = true;
            await LoadAsync();
        }

        private async Task LoadAsync(ulong? selectStatusId = null)
        {
            if (_isLoading)
                return;

            _isLoading = true;

            try
            {
                var statuses = await _ticketAdminApi.GetStatusesAsync();

                Statuses.Clear();
                StatusesGrid.SelectedItem = null;

                foreach (var item in statuses)
                    Statuses.Add(item);

                if (selectStatusId.HasValue)
                {
                    var match = Statuses.FirstOrDefault(x => x.Id == selectStatusId.Value);

                    if (match != null)
                    {
                        StatusesGrid.SelectedItem = match;
                        StatusesGrid.ScrollIntoView(match);
                    }
                }
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
        }

        private void StatusesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusButtons();
        }

        private void UpdateStatusButtons()
        {
            var selected = SelectedStatus;
            var hasSelection = selected != null;
            var isSystemRequired = IsSystemRequiredStatus(selected?.Name);

            var visibility = hasSelection
                ? Visibility.Visible
                : Visibility.Collapsed;

            MoveStatusUpButton.Visibility = visibility;
            MoveStatusDownButton.Visibility = visibility;
            EditStatusButton.Visibility = visibility;
            DeactivateStatusButton.Visibility = visibility;
            DeleteStatusButton.Visibility = visibility;

            if (!hasSelection)
                return;

            var index = Statuses.IndexOf(selected!);

            MoveStatusUpButton.IsEnabled = index > 0;
            MoveStatusDownButton.IsEnabled = index >= 0 && index < Statuses.Count - 1;

            EditStatusButton.IsEnabled = true;

            DeactivateStatusButton.IsEnabled =
                selected?.IsActive == true &&
                !isSystemRequired;

            DeleteStatusButton.IsEnabled =
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
                var created = await _ticketAdminApi.CreateStatusAsync(new CreateTicketStatusRequest
                {
                    Name = dialog.StatusName,
                    SortOrder = 0,
                    IsActive = dialog.StatusIsActive,
                    IsClosed = dialog.IsClosed,
                    IsFieldComplete = dialog.IsFieldComplete,
                    ShowInFilter = dialog.ShowInFilter,
                    IncludeInSummary = dialog.IncludeInSummary,
                    SendToDispatchTasks = dialog.SendToDispatchTasks,
                    IsWriteUpSubmitTarget = dialog.IsWriteUpSubmitTarget,
                    IsAssignmentPublishTarget = dialog.IsAssignmentPublishTarget,
                    IsUnassignmentTarget = dialog.IsUnassignmentTarget
                });

                await LoadAsync(created.Id);
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
                var selectedId = SelectedStatus.Id;

                await _ticketAdminApi.UpdateStatusAsync(new UpdateTicketStatusRequest
                {
                    Id = SelectedStatus.Id,
                    Name = dialog.StatusName,
                    SortOrder = SelectedStatus.SortOrder,
                    IsActive = dialog.StatusIsActive,
                    IsClosed = dialog.IsClosed,
                    IsFieldComplete = dialog.IsFieldComplete,
                    ShowInFilter = dialog.ShowInFilter,
                    IncludeInSummary = dialog.IncludeInSummary,
                    SendToDispatchTasks = dialog.SendToDispatchTasks,
                    IsWriteUpSubmitTarget = dialog.IsWriteUpSubmitTarget,
                    IsAssignmentPublishTarget = dialog.IsAssignmentPublishTarget,
                    IsUnassignmentTarget = dialog.IsUnassignmentTarget
                });

                await LoadAsync(selectedId);
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

        private async void DeleteStatus_Click(object sender, RoutedEventArgs e)
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

            var confirm = new DangerConfirmWindow(
                "Delete ticket status?",
                $"Permanently delete '{SelectedStatus.Name}'?\n\nIf this status is already used by tickets, delete will be blocked. If you only want to hide this status, use Deactivate instead.",
                "Delete")
            {
                Owner = Window.GetWindow(this)
            };

            if (confirm.ShowDialog() != true)
                return;

            try
            {
                await _ticketAdminApi.DeleteStatusAsync(SelectedStatus.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to delete ticket status.\n\n{ex.Message}",
                    "Delete Status Error",
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

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync(SelectedStatus?.Id);
        }

        private async void MoveStatusUp_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedStatusAsync(-1);
        }

        private async void MoveStatusDown_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedStatusAsync(1);
        }

        private async Task MoveSelectedStatusAsync(int offset)
        {
            var selected = SelectedStatus;
            if (selected == null)
                return;

            var currentIndex = Statuses.IndexOf(selected);
            var targetIndex = currentIndex + offset;

            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= Statuses.Count)
                return;

            var orderedIds = Statuses
                .Select(x => x.Id)
                .ToList();

            (orderedIds[currentIndex], orderedIds[targetIndex]) =
                (orderedIds[targetIndex], orderedIds[currentIndex]);

            try
            {
                await _ticketAdminApi.ReorderStatusesAsync(orderedIds);
                await LoadAsync(selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to reorder ticket statuses.\n\n{ex.Message}",
                    "Reorder Status Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}