#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.FieldTechnician;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.FieldTechnician.Panes
{
    public partial class FieldTechTasksPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<FieldTechTicketListItemDto>? OpenTicketRequested;

        public event Action<IReadOnlyList<FieldTechTicketListItemDto>>?
            OpenAllTicketsRequested;

        private readonly ApiClient _api = ClientAppSettings.CreateApiClient();

        private readonly bool _isLinemanMode;

        private bool _loadedOnce;
        private bool _busyLoading;
        private string _statusMessage = "Ready.";

        private readonly Dictionary<Button, CancellationTokenSource> _gridCopyFeedbackTokens = new();

        private readonly Dictionary<long, FieldTechExpandedTicketDetailsDto> _expandedDetailsByTicketId = new();
        private readonly HashSet<long> _expandedTicketIds = new();
        private readonly HashSet<long> _loadingExpandedTicketIds = new();

        public ObservableCollection<FieldTechTicketListItemDto> DailyAssignments { get; } = new();

        public ObservableCollection<FieldTechTicketListItemDto> OtherAssignedTickets { get; } = new();

        public int DailyAssignmentCount => DailyAssignments.Count;

        public int OtherAssignedTicketCount => OtherAssignedTickets.Count;

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public FieldTechTasksPaneView()
        : this(isLinemanMode: false)
        {
        }

        public FieldTechTasksPaneView(bool isLinemanMode)
        {
            _isLinemanMode = isLinemanMode;

            InitializeComponent();

            ApplyDisplayMode();

            DataContext = this;

            Loaded += async (_, __) =>
            {
                if (_loadedOnce)
                    return;

                _loadedOnce = true;
                await LoadTasksAsync();
            };
        }

        private void ApplyDisplayMode()
        {
            if (!_isLinemanMode)
                return;

            PaneSubtitleTextBlock.Text =
                "Other assignments first, followed by published assignments from Dispatch.";

            DailyAssignmentsTitleTextBlock.Text =
                "Assignments";

            DailyAssignmentsSubtitleTextBlock.Text =
                "Published assignments from Dispatch";

            OtherAssignmentsTitleTextBlock.Text =
                "Other Assignments";

            OtherAssignmentsSubtitleTextBlock.Text =
                "Active tickets assigned directly to you";

            /*
             * Row 2 is the larger 3* area. Row 4 is the smaller 2* area.
             * Only Lineman mode reverses the two cards.
             */
            Grid.SetRow(
                OtherAssignmentsCard,
                2);

            Grid.SetRow(
                DailyAssignmentsCard,
                4);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTasksAsync();
        }

        /*
         * Allows the Field Technician shell to request a fresh task list
         * whenever the technician returns to the Tasks pane.
         *
         * LoadTasksAsync already owns the busy guard and all API/offline
         * error handling, so callers do not need to duplicate that logic.
         */
        public Task RefreshAsync()
        {
            return LoadTasksAsync();
        }

        // Loads API-defined task sections while preserving the last successful result
        // whenever the API or field network is temporarily unavailable.
        private async Task LoadTasksAsync()
        {
            if (_busyLoading)
                return;

            try
            {
                _busyLoading = true;

                StatusMessage = "Loading assigned tasks...";
                ShowBusyOverlay(StatusMessage);

                var technician = await CurrentUserService
                    .LoadCurrentTechnicianAsync(forceRefresh: true);

                if (technician == null ||
                    string.IsNullOrWhiteSpace(technician.EmployeeId))
                {
                    ClearTaskCollections();

                    StatusMessage =
                        "No active technician record was found for the signed-in user.";

                    return;
                }

                var employeeId = Uri.EscapeDataString(
                    technician.EmployeeId);

                var response = await _api
                    .GetAsync<FieldTechTasksResponseDto>(
                        $"api/tickets/field-tech/tasks/{employeeId}");

                var nextDailyAssignments =
                    response?.DailyAssignments ??
                    new List<FieldTechTicketListItemDto>();

                var nextOtherAssignedTickets =
                    response?.OtherAssignedTickets ??
                    new List<FieldTechTicketListItemDto>();

                /*
                 * Do not clear currently visible data until the API has returned a
                 * complete successful response.
                 */
                ReplaceTaskCollections(
                    nextDailyAssignments,
                    nextOtherAssignedTickets);

                var technicianName =
                    !string.IsNullOrWhiteSpace(response?.TechnicianName)
                        ? response!.TechnicianName
                        : technician.Name;

                StatusMessage =
                    $"Loaded {DailyAssignments.Count} daily assignment(s) and " +
                    $"{OtherAssignedTickets.Count} other assigned ticket(s) for " +
                    $"{technicianName}.";
            }
            catch (ApiClient.ApiConnectionException)
            {
                StatusMessage =
                    "Offline — showing the last successfully loaded task list. " +
                    "Reconnect and click Refresh to try again.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusMessage =
                    $"Unable to refresh tasks. The server returned error " +
                    $"{ex.StatusCode}. Existing tasks were kept.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "An unexpected error occurred while refreshing tasks. " +
                    "Existing tasks were kept.";

                MessageBox.Show(
                    ex.Message,
                    "Field Technician Tasks",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _busyLoading = false;
                HideBusyOverlay();
            }
        }

        // Replaces task rows only after a successful API response so a failed refresh
        // never erases the technician's last available route or assigned-ticket list.
        private void ReplaceTaskCollections(
            IReadOnlyList<FieldTechTicketListItemDto> dailyAssignments,
            IReadOnlyList<FieldTechTicketListItemDto> otherAssignedTickets)
        {
            ResetGridCopyFeedback();
            ResetExpandedTaskRows();

            DailyAssignments.Clear();
            OtherAssignedTickets.Clear();

            foreach (var row in dailyAssignments)
                DailyAssignments.Add(row);

            foreach (var row in otherAssignedTickets)
                OtherAssignedTickets.Add(row);

            RefreshTaskCounts();
        }

        // Clears task rows, temporary copy confirmations, and expanded-row details
        // whenever fresh API results are loaded or the technician cannot be resolved.
        private void ClearTaskCollections()
        {
            ResetGridCopyFeedback();
            ResetExpandedTaskRows();

            DailyAssignments.Clear();
            OtherAssignedTickets.Clear();

            RefreshTaskCounts();
        }

        // Cancels any active copy-icon timers and returns affected cells to the
        // standard copy glyph before the visible task rows are replaced.
        private void ResetGridCopyFeedback()
        {
            foreach (var feedback in _gridCopyFeedbackTokens.ToList())
            {
                feedback.Value.Cancel();
                feedback.Value.Dispose();

                feedback.Key.Content = CreateGridFeedbackGlyph("CopyGlyph");
            }

            _gridCopyFeedbackTokens.Clear();
        }

        // Clears expanded-row state so refreshed task rows begin collapsed and reload
        // current Dispatch Notes and Site Notes only when reopened.
        private void ResetExpandedTaskRows()
        {
            _expandedDetailsByTicketId.Clear();
            _expandedTicketIds.Clear();
            _loadingExpandedTicketIds.Clear();

            CollapseVisibleDetailsRows(DailyAssignmentsGrid);
            CollapseVisibleDetailsRows(OtherAssignedTicketsGrid);
        }

        // Collapses any row containers currently rendered in a task grid.
        private static void CollapseVisibleDetailsRows(DataGrid grid)
        {
            foreach (var item in grid.Items)
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
                    continue;

                row.Tag = null;
                row.DetailsVisibility = Visibility.Collapsed;
            }
        }

        // Notifies the section headers after their API-provided collections change.
        private void RefreshTaskCounts()
        {
            OnPropertyChanged(nameof(DailyAssignmentCount));
            OnPropertyChanged(nameof(OtherAssignedTicketCount));
        }

        // Opens only the dispatcher-published daily route in route order.
        // Supplemental directly assigned tickets are intentionally excluded.
        private void OpenAll_Click(object sender, RoutedEventArgs e)
        {
            var tickets = DailyAssignments
                .Where(x =>
                    x.Id > 0 &&
                    !string.IsNullOrWhiteSpace(x.Site))
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList();

            if (tickets.Count == 0)
            {
                MessageBox.Show(
                    "There are no Daily Assignment sites to open.",
                    "Open All",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            OpenAllTicketsRequested?.Invoke(tickets);
        }

        // Restores the correct collapsed or expanded state when WPF creates or recycles
        // a task-row container during scrolling or re-rendering.
        private void TasksGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is not FieldTechTicketListItemDto ticket)
                return;

            e.Row.Tag = _expandedDetailsByTicketId.TryGetValue(ticket.Id, out var details)
                ? details
                : null;

            e.Row.DetailsVisibility = _expandedTicketIds.Contains(ticket.Id)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Expands one task row and lazy-loads its current Dispatch Notes and Site Notes
        // from the API the first time that row is opened.
        private async void ToggleTaskRowDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                FindVisualParent<DataGridRow>(button) is not DataGridRow row ||
                row.Item is not FieldTechTicketListItemDto ticket)
            {
                return;
            }

            if (row.DetailsVisibility == Visibility.Visible)
            {
                row.DetailsVisibility = Visibility.Collapsed;
                _expandedTicketIds.Remove(ticket.Id);
                return;
            }

            row.DetailsVisibility = Visibility.Visible;
            _expandedTicketIds.Add(ticket.Id);

            if (_expandedDetailsByTicketId.TryGetValue(ticket.Id, out var cachedDetails))
            {
                row.Tag = cachedDetails;
                return;
            }

            if (!_loadingExpandedTicketIds.Add(ticket.Id))
                return;

            try
            {
                var details = await _api.GetAsync<FieldTechExpandedTicketDetailsDto>(
                    $"api/tickets/field-tech/expanded-details/{ticket.Id}");

                if (details == null)
                    throw new InvalidOperationException("No expanded ticket details were returned.");

                _expandedDetailsByTicketId[ticket.Id] = details;

                if (row.Item is FieldTechTicketListItemDto currentTicket &&
                    currentTicket.Id == ticket.Id)
                {
                    row.Tag = details;
                }
            }
            catch (ApiClient.ApiConnectionException)
            {
                _expandedTicketIds.Remove(ticket.Id);
                row.DetailsVisibility = Visibility.Collapsed;

                StatusMessage =
                    "Offline — additional ticket details are unavailable. " +
                    "The existing task list was kept.";
            }
            catch (ApiClient.ApiException ex)
            {
                _expandedTicketIds.Remove(ticket.Id);
                row.DetailsVisibility = Visibility.Collapsed;

                StatusMessage =
                    $"Unable to load ticket details. The server returned error " +
                    $"{ex.StatusCode}.";
            }
            catch (Exception ex)
            {
                _expandedTicketIds.Remove(ticket.Id);
                row.DetailsVisibility = Visibility.Collapsed;

                MessageBox.Show(
                    ex.Message,
                    "Task Details",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _loadingExpandedTicketIds.Remove(ticket.Id);
            }
        }

        // Opens a double-clicked task site while ignoring interactions with chevron
        // and copy buttons embedded inside that task row.
        private void TasksGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            if (sender is not DataGrid grid ||
                grid.SelectedItem is not FieldTechTicketListItemDto selectedTicket)
            {
                return;
            }

            var site = (selectedTicket.Site ?? string.Empty).Trim();

            if (selectedTicket.Id <= 0 ||
                string.IsNullOrWhiteSpace(site))
            {
                return;
            }

            OpenTicketRequested?.Invoke(selectedTicket);
        }

        // Copies one Notification or Work Order value from either task grid and
        // confirms the action with a temporary checkmark on the clicked icon.
        private async void CopyGridValue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var value = (button.Tag?.ToString() ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return;

            Clipboard.SetText(value);

            await ShowGridCopyFeedbackAsync(button);
        }

        // Replaces only the clicked mini-copy icon with a checkmark for three seconds.
        // Separate timers allow copies in multiple rows or columns to confirm independently.
        private async Task ShowGridCopyFeedbackAsync(Button button)
        {
            if (_gridCopyFeedbackTokens.TryGetValue(button, out var existingToken))
            {
                existingToken.Cancel();
                existingToken.Dispose();
            }

            var feedbackToken = new CancellationTokenSource();
            _gridCopyFeedbackTokens[button] = feedbackToken;

            button.Content = CreateGridFeedbackGlyph("CheckGlyph");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), feedbackToken.Token);

                if (!feedbackToken.IsCancellationRequested)
                    button.Content = CreateGridFeedbackGlyph("CopyGlyph");
            }
            catch (TaskCanceledException)
            {
                // The same icon was clicked again, so its three-second timer restarted.
            }
            finally
            {
                if (_gridCopyFeedbackTokens.TryGetValue(button, out var currentToken) &&
                    ReferenceEquals(currentToken, feedbackToken))
                {
                    _gridCopyFeedbackTokens.Remove(button);
                    feedbackToken.Dispose();
                }
            }
        }

        // Creates the normal copy glyph or temporary checkmark using the pane's
        // existing task-grid resource styles.
        private TextBlock CreateGridFeedbackGlyph(string styleKey)
        {
            return new TextBlock
            {
                Style = (Style)FindResource(styleKey)
            };
        }

        // Finds an ancestor WPF control for embedded task-row button interactions.
        private static T? FindVisualParent<T>(DependencyObject? child)
            where T : DependencyObject
        {
            var current = child;

            while (current != null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void ShowBusyOverlay(string message)
        {
            if (BusyOverlay is null ||
                BusyOverlayMessageTextBlock is null)
            {
                return;
            }

            BusyOverlayMessageTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Loading..."
                : message;

            BusyOverlay.Visibility = Visibility.Visible;

            RefreshTasksButton.IsEnabled = false;
            OpenAllTasksButton.IsEnabled = false;
            DailyAssignmentsGrid.IsEnabled = false;
            OtherAssignedTicketsGrid.IsEnabled = false;
        }

        private void HideBusyOverlay()
        {
            if (BusyOverlay is null)
                return;

            BusyOverlay.Visibility = Visibility.Collapsed;

            RefreshTasksButton.IsEnabled = true;
            OpenAllTasksButton.IsEnabled = true;
            DailyAssignmentsGrid.IsEnabled = true;
            OtherAssignedTicketsGrid.IsEnabled = true;
        }
    }
}