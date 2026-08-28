using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using SmartGridSuite.Contracts.Dispatcher;
using SmartGridSuite.Contracts.Tickets;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.SiteNotes;
using System.Security.Principal;
using System.Windows.Threading;
using System.Threading.Tasks;


namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TaskPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ObservableCollection<DispatchTask> _tasks = new();
        private readonly TicketsApi _ticketsApi;
        private readonly TicketAdminApi _ticketAdminApi;

        private readonly SiteNotesApi _siteNotesApi;

        private readonly TechniciansApi _techniciansApi;

        private bool _suppressFilterEvents;
        private bool _filtersInitialized;
        private bool _hasLoadedOnce;

        private readonly DispatcherTimer _searchDebounceTimer;
        private readonly DispatcherTimer _idleRefreshTimer;

        private CancellationTokenSource? _taskQueryCts;

        private static readonly TimeSpan TaskIdleRefreshThreshold = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan MinimumTaskRefreshSpacing = TimeSpan.FromSeconds(5);

        private DateTime _lastTaskActivityUtc = DateTime.UtcNow;

        private DateTime _lastTaskRefreshUtc = DateTime.MinValue;

        private bool _silentRefreshInProgress;
        private int _activeTaskLoadCount;

        private string _lastAppliedTaskSearch = "";
        private string _lastAppliedTaskStatus = "All";

        private int _busyOverlayDepth;

        public bool HasSelectedTask => SelectedTask != null;

        private readonly HashSet<long> _expandedTaskTicketIds = new();

        private readonly HashSet<long> _updatingCloseoutItemIds = new();

        public ICollectionView TasksView { get; }

        private DispatchTask? _selectedTask;
        public DispatchTask? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (ReferenceEquals(_selectedTask, value))
                    return;

                _selectedTask = value;

                OnPropertyChanged(nameof(SelectedTask));
                OnPropertyChanged(nameof(HasSelectedTask));
            }
        }

        public bool IsSelectedTaskClosed
        {
            get
            {
                var status = SelectedTask?.Status ?? "";

                return status.Equals(
                           "Closed",
                           StringComparison.OrdinalIgnoreCase)
                    || status.Equals(
                           "Completed",
                           StringComparison.OrdinalIgnoreCase)
                    || status.Equals(
                           "Cancelled",
                           StringComparison.OrdinalIgnoreCase)
                    || status.Equals(
                           "Canceled",
                           StringComparison.OrdinalIgnoreCase);
            }
        }
        private void RestoreLastAppliedTaskFilters()
        {
            _searchDebounceTimer.Stop();

            _suppressFilterEvents = true;

            try
            {
                if (SearchBox != null)
                    SearchBox.Text = _lastAppliedTaskSearch;

                if (StatusFilter != null)
                    StatusFilter.SelectedItem = _lastAppliedTaskStatus;
            }
            finally
            {
                _suppressFilterEvents = false;
            }
        }

        public TaskPaneView()
        {
            InitializeComponent();
            DataContext = this;

            var api = ClientAppSettings.CreateApiClient();

            _ticketsApi = new TicketsApi(api);
            _ticketAdminApi = new TicketAdminApi(api);
            _siteNotesApi = new SiteNotesApi(api);
            _techniciansApi = new TechniciansApi(api);

            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            _idleRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };

            _idleRefreshTimer.Tick += IdleRefreshTimer_Tick;

            /*
             * Mouse/key activity resets the idle window.
             * We intentionally do not use MouseMove because simply moving
             * the pointer across the pane should not prevent refreshing forever.
             */
            PreviewMouseDown += TaskPaneView_PreviewMouseDown;
            PreviewMouseWheel += TaskPaneView_PreviewMouseWheel;
            PreviewKeyDown += TaskPaneView_PreviewKeyDown;

            IsVisibleChanged += TaskPaneView_IsVisibleChanged;
            Unloaded += TaskPaneView_Unloaded;

            TasksView = CollectionViewSource.GetDefaultView(_tasks);

            TasksView.SortDescriptions.Clear();
            TasksView.SortDescriptions.Add(
                new SortDescription(nameof(DispatchTask.OccurredAt), ListSortDirection.Descending));

            TasksGrid.ItemsSource = TasksView;

            _suppressFilterEvents = true;
            try
            {
                StatusFilter.ItemsSource = new[] { "All" };
                StatusFilter.SelectedIndex = 0;
            }
            finally
            {
                _suppressFilterEvents = false;
            }
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            RecordTaskActivity();

            _idleRefreshTimer.Start();

            /*
             * Cached/reloaded Tasks panes should immediately pick up
             * changes made by another dispatcher.
             */
            if (_hasLoadedOnce)
            {
                await TrySilentTaskRefreshAsync(
                    force: true);

                return;
            }

            _hasLoadedOnce = true;

            ShowBusyOverlay(
                "Loading dispatch task filters and task list...");

            try
            {
                await LoadFilterOptionsAsync();

                _filtersInitialized = true;

                await LoadTasksAsync();
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private void RecordTaskActivity()
        {
            _lastTaskActivityUtc =
                DateTime.UtcNow;

            /*
             * This is important:
             *
             * If a silent refresh is waiting on the API and the dispatcher
             * starts working again, cancel that request. This prevents its
             * eventual response from clearing/rebuilding the grid underneath
             * an active dispatcher.
             */
            if (_silentRefreshInProgress)
            {
                _taskQueryCts?.Cancel();
            }
        }

        private void TaskPaneView_PreviewMouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            RecordTaskActivity();
        }

        private void TaskPaneView_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            RecordTaskActivity();
        }

        private void TaskPaneView_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            RecordTaskActivity();
        }

        private async void IdleRefreshTimer_Tick(object? sender, EventArgs e)
        {
            await TrySilentTaskRefreshAsync(
                force: false);
        }

        private async Task TrySilentTaskRefreshAsync(bool force)
        {
            if (!_filtersInitialized ||
                !IsVisible ||
                _busyOverlayDepth > 0 ||
                _activeTaskLoadCount > 0 ||
                _silentRefreshInProgress ||
                _updatingCloseoutItemIds.Count > 0)
            {
                return;
            }

            /*
             * Never rebuild the grid while a dispatcher has a task expanded.
             * They may simply be reading a long write-up and therefore would
             * otherwise look "idle" even though they are actively using it.
             */
            if (_expandedTaskTicketIds.Count > 0)
                return;

            var now =
                DateTime.UtcNow;

            if (force)
            {
                /*
                 * Avoid duplicate refreshes caused by Loaded and
                 * IsVisibleChanged firing close together.
                 */
                if (now - _lastTaskRefreshUtc <
                    MinimumTaskRefreshSpacing)
                {
                    return;
                }
            }
            else
            {
                if (now - _lastTaskActivityUtc <
                    TaskIdleRefreshThreshold)
                {
                    return;
                }

                /*
                 * Once someone has been idle for a long period, refresh
                 * at most once per idle threshold rather than every
                 * 10-second timer tick.
                 */
                if (now - _lastTaskRefreshUtc <
                    TaskIdleRefreshThreshold)
                {
                    return;
                }
            }

            _silentRefreshInProgress =
                true;

            try
            {
                await LoadTasksAsync(
                    showBusyOverlay: false,
                    showErrors: false,
                    abandonIfUserBecomesActive: true);
            }
            finally
            {
                _silentRefreshInProgress =
                    false;
            }
        }

        private async void TaskPaneView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                _idleRefreshTimer.Stop();
                _searchDebounceTimer.Stop();

                /*
                 * Only cancel automatically when leaving the pane.
                 * The next visible load will create a fresh query token.
                 */
                _taskQueryCts?.Cancel();

                return;
            }

            RecordTaskActivity();

            _idleRefreshTimer.Start();

            if (_hasLoadedOnce &&
                _filtersInitialized &&
                IsLoaded)
            {
                await TrySilentTaskRefreshAsync(
                    force: true);
            }
        }

        private void TaskPaneView_Unloaded(object sender, RoutedEventArgs e)
        {
            _idleRefreshTimer.Stop();
            _searchDebounceTimer.Stop();

            _taskQueryCts?.Cancel();
        }

        private async Task LoadFilterOptionsAsync(CancellationToken ct = default)
        {
            if (StatusFilter == null)
                return;

            var statusFilter = StatusFilter;
            var previousStatus = statusFilter.SelectedItem as string ?? "All";

            _suppressFilterEvents = true;

            try
            {
                var statuses = await _ticketAdminApi.GetStatusesAsync(ct);

                var taskStatuses = statuses
                    .Where(x => x.IsActive && x.SendToDispatchTasks)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Name)
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var items = new List<string> { "All" };
                items.AddRange(taskStatuses);

                statusFilter.ItemsSource = items;

                statusFilter.SelectedItem = items.Contains(
                    previousStatus,
                    StringComparer.OrdinalIgnoreCase)
                        ? previousStatus
                        : "All";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load task filter options.\n\n{ex.Message}",
                    "Task Filters",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                statusFilter.ItemsSource = new[] { "All" };
                statusFilter.SelectedIndex = 0;
            }
            finally
            {
                _suppressFilterEvents = false;
            }
        }

        private DispatchTaskQueryRequest BuildDispatchTaskQueryRequest()
        {
            var selectedStatus = StatusFilter?.SelectedItem as string ?? "All";

            var applyStatusFilter =
                !selectedStatus.Equals("All", StringComparison.OrdinalIgnoreCase);

            return new DispatchTaskQueryRequest
            {
                Search = SearchBox?.Text?.Trim(),

                Statuses = applyStatusFilter
                    ? new List<string> { selectedStatus }
                    : new List<string>(),

                ApplyStatusFilter = applyStatusFilter,

                AssignedTech = "All",

                From = null,
                To = null,

                Skip = 0,
                Take = 2000
            };
        }

        private async Task LoadTasksAsync(
            CancellationToken ct = default,
            bool showBusyOverlay = true,
            bool showErrors = true,
            bool abandonIfUserBecomesActive = false)
        {
            if (!_filtersInitialized)
                return;

            _activeTaskLoadCount++;

            var activityAtQueryStart =
                _lastTaskActivityUtc;

            _taskQueryCts?.Cancel();
            _taskQueryCts?.Dispose();

            _taskQueryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var queryCt = _taskQueryCts.Token;

            if (showBusyOverlay)
            {
                ShowBusyOverlay(
                    "Loading dispatch tasks...");
            }

            var selectedTicketId = SelectedTask?.TicketId;

            try
            {
                var request = BuildDispatchTaskQueryRequest();
                var response =
                    await _ticketsApi.QueryDispatchTasksAsync(
                        request,
                        queryCt);

                /*
                 * A dispatcher became active while this silent request was in flight.
                 * Throw the response away instead of rebuilding the UI underneath them.
                 */
                if (abandonIfUserBecomesActive &&
                    _lastTaskActivityUtc != activityAtQueryStart)
                {
                    return;
                }

                _tasks.Clear();

                foreach (var item in response.Items
                             .OrderByDescending(x => x.OccurredAt)
                             .Select(MapDtoToModel))
                {
                    _tasks.Add(item);
                }

                var currentTicketIds = _tasks
                    .Select(x => x.TicketId)
                    .ToHashSet();

                _expandedTaskTicketIds.RemoveWhere(
                    ticketId =>
                        !currentTicketIds.Contains(ticketId));

                TasksView.Refresh();
                RestoreSelection(selectedTicketId);

                _lastTaskRefreshUtc =
                    DateTime.UtcNow;

                _lastAppliedTaskSearch = SearchBox?.Text?.Trim() ?? "";
                _lastAppliedTaskStatus = StatusFilter?.SelectedItem as string ?? "All";
            }
            catch (OperationCanceledException)
            {
                // Expected when search/filter values change quickly.
            }
            catch (Exception ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(
                        $"Failed to load dispatch tasks.\n\n{ex.Message}",
                        "Task Load Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                if (showBusyOverlay)
                {
                    HideBusyOverlay();
                }

                if (_activeTaskLoadCount > 0)
                {
                    _activeTaskLoadCount--;
                }
            }
        }

        private static WorkOrderClass ParseWorkOrderClass(string? value)
        {
            var v = (value ?? "").Trim();

            if (v.Equals("Cap", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("Capital", StringComparison.OrdinalIgnoreCase))
                return WorkOrderClass.Capital;

            if (v.Equals("Maint", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
                return WorkOrderClass.Maintenance;

            if (v.Equals("Dist", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("Distribution", StringComparison.OrdinalIgnoreCase))
                return WorkOrderClass.Distribution;

            return WorkOrderClass.Unknown;
        }

        private async Task EnsureTaskSiteNotesLoadedAsync(
            DispatchTask task,
            bool force = false)
        {
            if (task == null)
                return;

            var siteId =
                (task.Site ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(siteId))
                return;

            if (task.IsSiteNotesLoading)
                return;

            if (task.SiteNotesLoaded && !force)
                return;

            task.IsSiteNotesLoading = true;

            try
            {
                var notes =
                    await _siteNotesApi.GetBySiteAsync(
                        siteId);

                task.SiteNotes.Clear();

                foreach (var note in notes
                             .GroupBy(x => x.Id)
                             .Select(x => x.First())
                             .OrderByDescending(
                                 x => x.UpdatedAt ?? x.CreatedAt))
                {
                    task.SiteNotes.Add(note);
                }

                task.SiteNotesLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load site notes for {siteId}.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                task.IsSiteNotesLoading = false;
            }
        }

        private static DispatchTask MapDtoToModel(DispatchTaskListItemDto dto)
        {
            return new DispatchTask
            {
                TicketId = dto.TicketId,

                OccurredAt = dto.OccurredAt,
                Site = dto.Site ?? "",
                NotificationName = dto.NotificationName ?? "",
                Problem = dto.Problem ?? "",

                Tech = dto.Tech ?? "",

                Notification = dto.Notification ?? "",
                WorkOrder = dto.WorkOrder ?? "",
                WorkOrderClass = ParseWorkOrderClass(dto.WorkOrderType),

                ActionRequired = dto.ActionRequired ?? "",
                Notes = dto.Notes ?? "",

                DispatchRequestDetails = dto.DispatchRequestDetails ?? "",

                Status = dto.Status ?? "",

                SubmissionId = dto.SubmissionId,

                SubmittedAt = dto.SubmittedAt,

                SubmittedByName = dto.SubmittedByName ?? "",

                SubmittedWriteUp = dto.SubmittedWriteUp ?? "",

                WriteUpFlags = dto.WriteUpFlags ??
                    new List<string>(),

                ReferToOptions = dto.ReferToOptions ??
                    new List<string>(),

                CloseoutChecklistItems = (dto.CloseoutChecklistItems ??
                    new List<SmartGridSuite.Contracts.Tickets.DispatchCloseoutChecklistItemDto>())
                        .Select(x => new DispatchCloseoutChecklistItem{
                            Id = x.Id,

                            SubmissionId =
                                x.SubmissionId,

                            DefinitionId =
                                x.DefinitionId,

                            DisplayName =
                                x.DisplayName ?? "",

                            SortOrder =
                                x.SortOrder,

                            IsRequired =
                                x.IsRequired,

                            ConditionType =
                                x.ConditionType ?? "",

                            WriteUpFlagId =
                                x.WriteUpFlagId,

                            ReferToOptionId =
                                x.ReferToOptionId,

                            IsCompleted =
                                x.IsCompleted,

                            CompletedBy =
                                x.CompletedBy ?? "",

                            CompletedAt =
                                x.CompletedAt
                            })
                        .ToList(),

                RequiredChecklistRemaining = dto.RequiredChecklistRemaining,

                CanMarkClosed = dto.CanMarkClosed,

                // Legacy compatibility only. No longer shown in the new Tasks UI.
                Category = dto.Category ?? ""
            };
        }

        private static DispatchTicket MapTicketDtoToModel(TicketListItemDto dto)
        {
            var rawWorkOrderType = (dto.WorkOrderClass ?? "").Trim();

            return new DispatchTicket
            {
                Id = dto.Id,
                Site = dto.Site,
                NotificationName = dto.NotificationName ?? "",
                Notification = dto.Notification ?? "",
                Status = dto.Status,
                AssignedTech = dto.AssignedTech,
                CreatedAt = dto.CreatedAt,
                LastActivityAt = dto.LastActivityAt,
                CurrentWorkOrder = dto.CurrentWorkOrder ?? "",
                WorkOrderType = rawWorkOrderType,
                WoClass = ParseWorkOrderClass(rawWorkOrderType),
                GroupCode = dto.GroupCode ?? "",
                PriorityDays = dto.PriorityDays,
                Problem = dto.Problem ?? "",
                Notes = dto.Notes ?? "",
                DispatchNotes = dto.DispatchNotes ?? "",
                CreatedBy = dto.CreatedBy ?? "",
                Summary = dto.Problem ?? "",
                TaskCategoryId = dto.TaskCategoryId,
                TaskCategoryName = dto.TaskCategoryName ?? "",
                ActionRequiredOverride = dto.ActionRequiredOverride ?? ""
            };
        }

        private void RestoreSelection(long? ticketId)
        {
            if (!ticketId.HasValue || ticketId.Value <= 0)
                return;

            var found =
                _tasks.FirstOrDefault(
                    x => x.TicketId == ticketId.Value);

            if (found != null)
            {
                TasksGrid.SelectedItem = found;
                TasksGrid.ScrollIntoView(found);
                return;
            }

            TasksGrid.SelectedItem = null;
            SelectedTask = null;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            RecordTaskActivity();

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            if (!_filtersInitialized)
                return;


            await LoadTasksAsync();
        }

        private async void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            RecordTaskActivity();

            await LoadTasksAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {

            ShowBusyOverlay("Refreshing dispatch tasks...");

            try
            {
                await LoadFilterOptionsAsync();
                await LoadTasksAsync();
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private async void ClearTaskFilters_Click(object sender, RoutedEventArgs e)
        {

            _searchDebounceTimer.Stop();

            _suppressFilterEvents = true;

            try
            {
                SearchBox.Text = "";
                StatusFilter.SelectedItem = "All";
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            await LoadTasksAsync();
        }

        private void TasksGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            SelectedTask =
                TasksGrid.SelectedItem as DispatchTask;
        }

        private void TasksGrid_LoadingRow(
            object sender,
            DataGridRowEventArgs e)
        {
            if (e.Row.Item is not DispatchTask task)
                return;

            e.Row.DetailsVisibility =
                _expandedTaskTicketIds.Contains(task.TicketId)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private async void ToggleTaskRowDetails_Click(
             object sender,
             RoutedEventArgs e)
        {
            if (sender is not Button button ||
                FindVisualParent<DataGridRow>(button)
                    is not DataGridRow row ||
                row.Item is not DispatchTask task)
            {
                return;
            }

            ToggleTaskRowDetails(
                row,
                task);

            if (row.DetailsVisibility == Visibility.Visible)
            {
                await EnsureTaskSiteNotesLoadedAsync(task);
            }
        }

        private async void TasksGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
                return;

            /*
             * Only treat a double-click on an actual grid cell as
             * a row expand/collapse action.
             *
             * This prevents double-clicking controls inside the
             * expanded details area from collapsing the row.
             */
            var cell =
                FindVisualParent<DataGridCell>(source);

            if (cell == null)
                return;

            /*
             * Do not steal double-clicks from the existing
             * expand/copy buttons inside cells.
             */
            if (FindVisualParent<Button>(source) != null)
                return;

            var row =
                FindVisualParent<DataGridRow>(cell);

            if (row?.Item is not DispatchTask task)
                return;

            ToggleTaskRowDetails(
                row,
                task);

            if (row.DetailsVisibility == Visibility.Visible)
            {
                await EnsureTaskSiteNotesLoadedAsync(task);
            }

            e.Handled = true;
        }

        private void ToggleTaskRowDetails(
            DataGridRow row,
            DispatchTask task)
        {
            TasksGrid.SelectedItem =
                task;

            if (row.DetailsVisibility ==
                Visibility.Visible)
            {
                row.DetailsVisibility =
                    Visibility.Collapsed;

                _expandedTaskTicketIds.Remove(
                    task.TicketId);

                return;
            }

            row.DetailsVisibility =
                Visibility.Visible;

            _expandedTaskTicketIds.Add(
                task.TicketId);
        }

        private async void CopySubmittedWriteUp_Click(
            object sender,
            RoutedEventArgs e)
        {
            await CopyGridValueAsync(
                sender as Button);
        }

        private async void CopyNotification_Click(object sender, RoutedEventArgs e)
        {
            await CopyGridValueAsync(sender as Button);
        }

        private async void CopyWorkOrder_Click(object sender, RoutedEventArgs e)
        {
            await CopyGridValueAsync(sender as Button);
        }

        private async Task CopyGridValueAsync(
            Button? button)
        {
            if (button?.Tag is not string value ||
                string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Clipboard.SetText(value);

            var originalContent =
                button.Content;

            button.Content =
                new TextBlock
                {
                    Style =
                        TryFindResource("CheckGlyph")
                            as Style
                };

            button.IsEnabled =
                false;

            try
            {
                await Task.Delay(3000);
            }
            finally
            {
                button.Content =
                    originalContent;

                button.IsEnabled =
                    true;
            }
        }

        private async void DispatchCloseoutItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox ||
                checkBox.DataContext
                    is not DispatchCloseoutChecklistItem checklistItem ||
                FindVisualParent<DataGridRow>(checkBox)
                    is not DataGridRow row ||
                row.Item is not DispatchTask task)
            {
                return;
            }

            if (task.TicketId <= 0 ||
                checklistItem.Id <= 0)
            {
                checklistItem.IsCompleted =
                    !checklistItem.IsCompleted;

                return;
            }

            if (!_updatingCloseoutItemIds.Add(
                    checklistItem.Id))
            {
                return;
            }

            var requestedState =
                checkBox.IsChecked == true;

            checkBox.IsEnabled =
                false;

            try
            {
                var result =
                    await _ticketsApi
                        .UpdateDispatchCloseoutChecklistItemAsync(
                            task.TicketId,
                            checklistItem.Id,
                            requestedState,
                            Environment.UserName);

                if (result == null)
                {
                    throw new InvalidOperationException(
                        "The server did not return the updated checklist item.");
                }

                checklistItem.IsCompleted =
                    result.IsCompleted;

                checklistItem.CompletedBy =
                    result.CompletedBy ?? "";

                checklistItem.CompletedAt =
                    result.CompletedAt;

                task.RequiredChecklistRemaining =
                    task.CloseoutChecklistItems.Count(
                        x =>
                            x.IsRequired &&
                            !x.IsCompleted);

                task.CanMarkClosed =
                    task.RequiredChecklistRemaining == 0;

                task.RefreshChecklistProgress();
            }
            catch (Exception ex)
            {
                /*
                 * The checkbox has already visually toggled by the time Click fires.
                 * Restore the last persisted state when the API update fails.
                 */
                checklistItem.IsCompleted =
                    !requestedState;

                MessageBox.Show(
                    $"Failed to update the Dispatch closeout checklist.\n\n{ex.Message}",
                    "Dispatch Closeout Checklist",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _updatingCloseoutItemIds.Remove(
                    checklistItem.Id);

                checkBox.IsEnabled =
                    true;
            }
        }

        private async void OpenNotification_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is DispatchTask task)
            {
                SelectedTask = task;
                TasksGrid.SelectedItem = task;
            }

            if (SelectedTask == null)
                return;

            if (SelectedTask.TicketId <= 0)
            {
                MessageBox.Show(
                    "This task is missing its ticket ID and cannot be opened.",
                    "Open Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dto = await _ticketsApi.GetTicketByIdAsync(SelectedTask.TicketId);

                if (dto == null)
                {
                    MessageBox.Show(
                        "The ticket for this task could not be found.",
                        "Open Ticket",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var ticket = MapTicketDtoToModel(dto);

                var technicians =
                    await _techniciansApi.GetTechniciansAsync(
                        includeInactive: false);

                var techNames = technicians
                    .Where(x =>
                        x.IsActive &&
                        x.RoleCodes.Any(role =>
                            role.Equals(
                                "Technician",
                                StringComparison.OrdinalIgnoreCase)))
                    .Select(x => x.Name?.Trim())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .Cast<string>()
                    .ToList();

                var writeUpHistory =
                    await _ticketsApi.GetWriteUpHistoryAsync(
                        SelectedTask.TicketId);

                var win =
                    new NewTicketWindow(
                        _ticketsApi,
                        techNames,
                        ticket,
                        writeUpHistory)
                    {
                        Owner = Window.GetWindow(this)
                    };

                if (win.ShowDialog() != true)
                    return;

                await LoadFilterOptionsAsync();
                await LoadTasksAsync();

                SelectedTask = null;
                TasksGrid.SelectedItem = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open ticket.\n\n{ex.Message}",
                    "Open Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void MarkClosed_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.DataContext is DispatchTask task)
            {
                SelectedTask = task;
                TasksGrid.SelectedItem = task;
            }

            if (SelectedTask == null)
                return;

            if (SelectedTask.IsClosed)
                return;

            if (SelectedTask.TicketId <= 0)
            {
                MessageBox.Show(
                    "This task is missing its ticket ID and cannot be closed.",
                    "Mark Closed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!SelectedTask.CanMarkClosed)
            {
                MessageBox.Show(
                    SelectedTask.RequiredChecklistRemaining == 1
                        ? "Complete the remaining required Dispatch closeout checklist item before closing this ticket."
                        : $"Complete the {SelectedTask.RequiredChecklistRemaining} remaining required Dispatch closeout checklist items before closing this ticket.",
                    "Mark Closed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var confirm = MessageBox.Show(
                $"Close the ticket for site {SelectedTask.Site}?\n\n" +
                "This will move the ticket to the configured closed status and clear its outstanding dispatch action.",
                "Confirm Ticket Closure",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await _ticketsApi.CloseDispatchTaskAsync(SelectedTask.TicketId);

                SelectedTask = null;
                TasksGrid.SelectedItem = null;

                await LoadTasksAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to close ticket.\n\n{ex.Message}",
                    "Mark Closed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static T? FindVisualParent<T>(
            DependencyObject? child)
            where T : DependencyObject
        {
            var current = child;

            while (current != null)
            {
                if (current is T match)
                    return match;

                current =
                    VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static string GetTaskSiteNoteUserName()
        {
            var windowsName =
                WindowsIdentity.GetCurrent()?.Name;

            if (!string.IsNullOrWhiteSpace(windowsName))
            {
                var clean =
                    windowsName.Trim();

                var slashIndex =
                    clean.LastIndexOf('\\');

                if (slashIndex >= 0 &&
                    slashIndex < clean.Length - 1)
                {
                    clean =
                        clean[(slashIndex + 1)..];
                }

                if (!string.IsNullOrWhiteSpace(clean))
                    return clean;
            }

            return string.IsNullOrWhiteSpace(
                Environment.UserName)
                    ? "Unknown"
                    : Environment.UserName;
        }

        private async void AddTaskSiteNote_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not DispatchTask task)
            {
                return;
            }

            var siteId =
                (task.Site ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return;

            var win =
                new SiteNoteEditorWindow(siteId)
                {
                    Owner = Window.GetWindow(this)
                };

            if (win.ShowDialog() != true)
                return;

            try
            {
                await _siteNotesApi.CreateAsync(
                    new CreateSiteNoteRequest
                    {
                        SiteId = siteId,
                        NoteType = win.NoteType,
                        NoteText = win.NoteText,
                        CreatedBy =
                            GetTaskSiteNoteUserName()
                    });

                await EnsureTaskSiteNotesLoadedAsync(
                    task,
                    force: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to add site note.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void EditTaskSiteNote_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not SiteNoteDto note ||
                FindVisualParent<DataGridRow>(button)
                    is not DataGridRow row ||
                row.Item is not DispatchTask task)
            {
                return;
            }

            var siteId =
                (task.Site ?? string.Empty).Trim();

            var win =
                new SiteNoteEditorWindow(
                    siteId,
                    note)
                {
                    Owner = Window.GetWindow(this)
                };

            if (win.ShowDialog() != true)
                return;

            try
            {
                await _siteNotesApi.UpdateAsync(
                    new UpdateSiteNoteRequest
                    {
                        Id = note.Id,
                        NoteType = win.NoteType,
                        NoteText = win.NoteText,
                        UpdatedBy =
                            GetTaskSiteNoteUserName()
                    });

                await EnsureTaskSiteNotesLoadedAsync(
                    task,
                    force: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update site note.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void DeleteTaskSiteNote_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not SiteNoteDto note ||
                FindVisualParent<DataGridRow>(button)
                    is not DataGridRow row ||
                row.Item is not DispatchTask task)
            {
                return;
            }

            var confirm =
                MessageBox.Show(
                    "Delete this site note?",
                    "Delete Site Note",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await _siteNotesApi.DeleteAsync(
                    note.Id,
                    GetTaskSiteNoteUserName());

                await EnsureTaskSiteNotesLoadedAsync(
                    task,
                    force: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to delete site note.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowBusyOverlay(string message)
        {
            _busyOverlayDepth++;

            if (BusyOverlay is null ||
                BusyOverlayMessageTextBlock is null)
            {
                return;
            }

            BusyOverlayMessageTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Loading..."
                : message;

            BusyOverlay.Visibility = Visibility.Visible;
            SetTaskPaneControlsEnabled(false);
        }

        private void HideBusyOverlay()
        {
            if (_busyOverlayDepth > 0)
                _busyOverlayDepth--;

            if (_busyOverlayDepth > 0)
                return;

            if (BusyOverlay is null)
                return;

            BusyOverlay.Visibility = Visibility.Collapsed;
            SetTaskPaneControlsEnabled(true);
        }

        private void SetTaskPaneControlsEnabled(bool enabled)
        {
            SearchBox.IsEnabled = enabled;
            StatusFilter.IsEnabled = enabled;
            ClearTaskFiltersButton.IsEnabled = enabled;
            RefreshTasksButton.IsEnabled = enabled;
            TasksGrid.IsEnabled = enabled;
        }
    }
}
