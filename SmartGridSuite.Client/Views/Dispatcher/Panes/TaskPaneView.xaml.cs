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


namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TaskPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ObservableCollection<DispatchTask> _tasks = new();
        private readonly TicketsApi _ticketsApi;
        private readonly TicketAdminApi _ticketAdminApi;

        private bool _suppressFilterEvents;
        private bool _filtersInitialized;
        private bool _hasLoadedOnce;

        private readonly DispatcherTimer _searchDebounceTimer;
        private CancellationTokenSource? _taskQueryCts;

        private bool _suppressTaskSelectionChange;

        private string _lastAppliedTaskSearch = "";
        private string _lastAppliedTaskStatus = "All";

        private int _busyOverlayDepth;

        public bool HasSelectedTask => SelectedTask != null;

        private bool _detailsOpen;

        private readonly SiteNotesApi _siteNotesApi;
        private readonly TechniciansApi _techniciansApi;
        private readonly ObservableCollection<SiteNoteDto> _selectedTaskSiteNotes = new();
        private readonly Dictionary<string, string> _createdByDisplayByUserId = new(StringComparer.OrdinalIgnoreCase);

        private DispatchTicket? _selectedTaskTicket;
        private string _selectedTaskOriginalDispatchNotes = "";
        private string _selectedTaskOriginalTechNotes = "";

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

                UpdateTaskToolbarButtons();
            }
        }

        public DispatchTicket? SelectedTaskTicket
        {
            get => _selectedTaskTicket;
            private set
            {
                if (ReferenceEquals(_selectedTaskTicket, value))
                    return;

                _selectedTaskTicket = value;

                OnPropertyChanged(nameof(SelectedTaskTicket));
                OnPropertyChanged(nameof(SelectedTaskCreatedByDisplay));
                OnPropertyChanged(nameof(SelectedTaskActionRequiredDisplay));
                OnPropertyChanged(nameof(IsSelectedTaskTicketClosed));
                OnPropertyChanged(nameof(CanEditSelectedTaskTicket));

                UpdateTaskSaveButtonState();
            }
        }

        public ObservableCollection<SiteNoteDto> SelectedTaskSiteNotes => _selectedTaskSiteNotes;

        public int SelectedTaskSiteNotesCount => _selectedTaskSiteNotes.Count;

        public string SelectedTaskCreatedByDisplay
            => ResolveCreatedByDisplay(SelectedTaskTicket?.CreatedBy);

        public string SelectedTaskActionRequiredDisplay
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SelectedTaskTicket?.ActionRequiredOverride))
                    return SelectedTaskTicket.ActionRequiredOverride;

                return SelectedTask?.ActionRequired ?? "";
            }
        }

        public bool IsSelectedTaskTicketClosed
        {
            get
            {
                var status = SelectedTaskTicket?.Status ?? SelectedTask?.Status ?? "";

                return status.Equals("Closed", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Canceled", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool CanEditSelectedTaskTicket => SelectedTaskTicket != null && !IsSelectedTaskTicketClosed;

        private bool HasUnsavedSelectedTaskDispatchNotes
        {
            get
            {
                if (SelectedTaskTicket == null || IsSelectedTaskTicketClosed)
                    return false;

                var dispatchNotesChanged =
                    !string.Equals(
                        SelectedTaskTicket.DispatchNotes ?? "",
                        _selectedTaskOriginalDispatchNotes,
                        StringComparison.Ordinal);

                var techWriteUpsChanged =
                    !string.Equals(
                        SelectedTaskTicket.Notes ?? "",
                        _selectedTaskOriginalTechNotes,
                        StringComparison.Ordinal);

                return dispatchNotesChanged || techWriteUpsChanged;
            }
        }

        private bool ConfirmDiscardUnsavedSelectedTaskDispatchNotes()
        {
            if (!HasUnsavedSelectedTaskDispatchNotes)
                return true;

            var result = MessageBox.Show(
                "You have unsaved note changes.\n\n" +
                "Discard changes to Dispatch Notes / Tech Write-Ups and continue?",
                "Unsaved Notes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return false;

            if (SelectedTaskTicket != null)
            {
                SelectedTaskTicket.DispatchNotes = _selectedTaskOriginalDispatchNotes;
                SelectedTaskTicket.Notes = _selectedTaskOriginalTechNotes;

                if (TaskDispatchNotesTextBox != null)
                    TaskDispatchNotesTextBox.Text = _selectedTaskOriginalDispatchNotes;

                if (TaskTechWriteUpsTextBox != null)
                    TaskTechWriteUpsTextBox.Text = _selectedTaskOriginalTechNotes;

                UpdateTaskSaveButtonState();
            }

            return true;
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

            UpdateDetailsVisibility();
            UpdateTaskToolbarButtons();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedOnce)
                return;

            _hasLoadedOnce = true;

            ShowBusyOverlay("Loading dispatch task filters and task list...");

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

        private async Task LoadTasksAsync(CancellationToken ct = default)
        {
            if (!_filtersInitialized)
                return;

            _taskQueryCts?.Cancel();
            _taskQueryCts?.Dispose();

            _taskQueryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var queryCt = _taskQueryCts.Token;

            ShowBusyOverlay("Loading dispatch tasks...");

            var selectedTicketId = SelectedTask?.TicketId;

            try
            {
                var request = BuildDispatchTaskQueryRequest();
                var response = await _ticketsApi.QueryDispatchTasksAsync(request, queryCt);

                if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
                {
                    RestoreLastAppliedTaskFilters();
                    return;
                }

                _tasks.Clear();

                foreach (var item in response.Items
                             .OrderByDescending(x => x.OccurredAt)
                             .Select(MapDtoToModel))
                {
                    _tasks.Add(item);
                }

                TasksView.Refresh();
                RestoreSelection(selectedTicketId);

                _lastAppliedTaskSearch = SearchBox?.Text?.Trim() ?? "";
                _lastAppliedTaskStatus = StatusFilter?.SelectedItem as string ?? "All";
            }
            catch (OperationCanceledException)
            {
                // Expected when search/filter values change quickly.
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load dispatch tasks.\n\n{ex.Message}",
                    "Task Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
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

                Status = dto.Status ?? "",

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
            {
                UpdateDetailsVisibility();
                return;
            }

            var found = _tasks.FirstOrDefault(x => x.TicketId == ticketId.Value);

            if (found != null)
            {
                TasksGrid.SelectedItem = found;
                TasksGrid.ScrollIntoView(found);
            }
            else
            {
                TasksGrid.SelectedItem = null;
                SelectedTask = null;
                SelectedTaskTicket = null;

                _detailsOpen = false;
                UpdateDetailsVisibility();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            if (!_filtersInitialized)
                return;

            if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
            {
                RestoreLastAppliedTaskFilters();
                return;
            }

            await LoadTasksAsync();
        }

        private async void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
            {
                RestoreLastAppliedTaskFilters();
                return;
            }

            await LoadTasksAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
                return;

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
            if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
                return;

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

        private async void TasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressTaskSelectionChange)
                return;

            var newlySelectedTask = TasksGrid.SelectedItem as DispatchTask;

            var isChangingOpenTask =
                _detailsOpen &&
                SelectedTask != null &&
                newlySelectedTask?.TicketId != SelectedTask.TicketId;

            if (isChangingOpenTask &&
                !ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
            {
                _suppressTaskSelectionChange = true;

                try
                {
                    TasksGrid.SelectedItem = SelectedTask;

                    if (SelectedTask != null)
                        TasksGrid.ScrollIntoView(SelectedTask);
                }
                finally
                {
                    _suppressTaskSelectionChange = false;
                }

                return;
            }

            SelectedTask = newlySelectedTask;

            if (!_detailsOpen)
            {
                SelectedTaskTicket = null;
                _selectedTaskSiteNotes.Clear();
                OnPropertyChanged(nameof(SelectedTaskSiteNotesCount));
                UpdateDetailsVisibility();
                return;
            }

            UpdateDetailsVisibility();

            if (SelectedTask != null)
                await LoadTaskDetailsForSelectedTaskAsync();
        }

        private async void TasksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject))
                return;

            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);

            if (row?.Item is not DispatchTask task)
                return;

            // SelectionChanged handles the unsaved-notes decision first.
            // If changing rows was cancelled, do not reopen the clicked row here.
            if (SelectedTask?.TicketId != task.TicketId ||
                (TasksGrid.SelectedItem as DispatchTask)?.TicketId != task.TicketId)
            {
                return;
            }

            _detailsOpen = true;
            UpdateDetailsVisibility();

            await LoadTaskDetailsForSelectedTaskAsync();
        }

        private async void OpenTaskDetails_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null)
                return;

            _detailsOpen = true;
            UpdateDetailsVisibility();

            await LoadTaskDetailsForSelectedTaskAsync();
        }

        private void CloseDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
                return;

            _detailsOpen = false;
            SelectedTask = null;
            SelectedTaskTicket = null;
            TasksGrid.SelectedItem = null;

            _selectedTaskOriginalDispatchNotes = "";
            _selectedTaskOriginalTechNotes = "";
            _selectedTaskSiteNotes.Clear();
            OnPropertyChanged(nameof(SelectedTaskSiteNotesCount));

            CollapseTaskDetailExpanders();
            UpdateDetailsVisibility();
            UpdateTaskSaveButtonState();
        }

        private void UpdateDetailsVisibility()
        {
            if (!_detailsOpen || SelectedTask == null)
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
                DetailsCol.Width = new GridLength(0);

                if (DetailsSplitter != null)
                    DetailsSplitter.Visibility = Visibility.Collapsed;

                return;
            }

            DetailsCol.Width = new GridLength(500);
            DetailsPanel.Visibility = Visibility.Visible;

            if (DetailsSplitter != null)
                DetailsSplitter.Visibility = Visibility.Visible;
        }

        private void UpdateTaskToolbarButtons()
        {
            var hasSelection = SelectedTask != null;
            var canCloseSelectedTask =
                hasSelection &&
                !HasUnsavedSelectedTaskDispatchNotes &&
                !IsSelectedTaskTicketClosed;

            if (OpenTaskDetailsButton != null)
                OpenTaskDetailsButton.IsEnabled = hasSelection;

            if (OpenTaskTicketButton != null)
                OpenTaskTicketButton.IsEnabled = hasSelection;

            if (MarkTaskClosedButton != null)
                MarkTaskClosedButton.IsEnabled = canCloseSelectedTask;

            if (MarkDetailsClosedButton != null)
                MarkDetailsClosedButton.IsEnabled = canCloseSelectedTask;
        }

        private async void CopyNotification_Click(object sender, RoutedEventArgs e)
        {
            await CopyGridValueAsync(sender as Button);
        }

        private async void CopyWorkOrder_Click(object sender, RoutedEventArgs e)
        {
            await CopyGridValueAsync(sender as Button);
        }

        private async Task CopyGridValueAsync(Button? button)
        {
            if (button?.Tag is not string value || string.IsNullOrWhiteSpace(value))
                return;

            Clipboard.SetText(value);

            var originalContent = button.Content;

            button.Content = new TextBlock
            {
                Text = "✓",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.IsEnabled = false;

            await Task.Delay(3000);

            button.Content = originalContent;
            button.IsEnabled = true;
        }

        private async void CopySummary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTaskTicket == null)
            {
                MessageBox.Show(
                    "Ticket details are still loading. Try again in a moment.",
                    "Copy Write-Up",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var writeUp = (SelectedTaskTicket.Notes ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(writeUp))
            {
                MessageBox.Show(
                    "There is no tech write-up to copy for this ticket.",
                    "Copy Write-Up",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(writeUp);

            if (sender is Button button)
            {
                var originalContent = button.Content;

                button.Content = "Copied!";
                button.IsEnabled = false;

                await Task.Delay(900);

                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }

        private async void OpenNotification_Click(object sender, RoutedEventArgs e)
        {
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

            if (!ConfirmDiscardUnsavedSelectedTaskDispatchNotes())
                return;

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

                var win = new NewTicketWindow(_ticketsApi, Enumerable.Empty<string>(), ticket)
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.ShowDialog() != true)
                    return;

                await LoadFilterOptionsAsync();
                await LoadTasksAsync();

                _detailsOpen = false;
                SelectedTask = null;
                TasksGrid.SelectedItem = null;
                UpdateDetailsVisibility();
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

        private async void MarkClosed_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null)
                return;

            if (HasUnsavedSelectedTaskDispatchNotes)
            {
                MessageBox.Show(
                    "You have unsaved Dispatch Notes.\n\nSave your notes before marking this ticket closed.",
                    "Save Dispatch Notes First",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (SelectedTask.TicketId <= 0)
            {
                MessageBox.Show(
                    "This task is missing its ticket ID and cannot be closed.",
                    "Mark Closed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

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

                _detailsOpen = false;
                SelectedTask = null;
                SelectedTaskTicket = null;
                TasksGrid.SelectedItem = null;

                await LoadTasksAsync();

                UpdateDetailsVisibility();
                UpdateTaskToolbarButtons();
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

        private static bool IsInsideButton(DependencyObject? source)
        {
            return FindVisualParent<Button>(source) != null;
        }

        private static T? FindVisualParent<T>(DependencyObject? source)
            where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T match)
                    return match;

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private async Task LoadTaskDetailsForSelectedTaskAsync()
        {
            var task = SelectedTask;

            SelectedTaskTicket = null;
            _selectedTaskSiteNotes.Clear();
            OnPropertyChanged(nameof(SelectedTaskSiteNotesCount));

            _selectedTaskOriginalDispatchNotes = "";
            _selectedTaskOriginalTechNotes = "";

            CollapseTaskDetailExpanders();

            if (task == null || task.TicketId <= 0)
                return;

            try
            {
                await LoadKnownTechAliasesAsync();

                var dto = await _ticketsApi.GetTicketByIdAsync(task.TicketId);

                if (dto == null)
                    return;

                var ticket = MapTicketDtoToModel(dto);

                SelectedTaskTicket = ticket;

                _selectedTaskOriginalDispatchNotes = ticket.DispatchNotes ?? "";
                _selectedTaskOriginalTechNotes = ticket.Notes ?? "";

                await LoadSiteNotesForSelectedTaskAsync(ticket.Site);

                UpdateTaskSaveButtonState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load task details.\n\n{ex.Message}",
                    "Task Details",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task LoadSiteNotesForSelectedTaskAsync(string? site)
        {
            _selectedTaskSiteNotes.Clear();
            OnPropertyChanged(nameof(SelectedTaskSiteNotesCount));

            site = (site ?? "").Trim();

            if (string.IsNullOrWhiteSpace(site))
                return;

            try
            {
                var notes = await _siteNotesApi.GetBySiteAsync(site);

                foreach (var note in notes)
                    _selectedTaskSiteNotes.Add(note);

                OnPropertyChanged(nameof(SelectedTaskSiteNotesCount));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load site notes.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task LoadKnownTechAliasesAsync()
        {
            try
            {
                var techs = await _techniciansApi.GetTechniciansAsync();

                _createdByDisplayByUserId.Clear();

                foreach (var tech in techs)
                {
                    var displayName =
                        TryReadStringProperty(tech, "Name", "DisplayName", "FullName")?.Trim();

                    var userId =
                        TryReadStringProperty(tech, "UserId", "UserName", "Username", "WindowsUserName", "NetworkUserId", "Login", "SamAccountName")?.Trim();

                    var employeeId =
                        TryReadStringProperty(tech, "EmployeeId", "EmployeeID", "EmpId", "BadgeNumber")?.Trim();

                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        AddCreatedByDisplayAlias(displayName, displayName);
                        AddCreatedByDisplayAlias(userId, displayName);
                        AddCreatedByDisplayAlias(employeeId, displayName);
                    }
                }

                OnPropertyChanged(nameof(SelectedTaskCreatedByDisplay));
            }
            catch
            {
                // Do not block task details if technician alias lookup fails.
            }
        }

        private static string? TryReadStringProperty(object obj, params string[] propertyNames)
        {
            var type = obj.GetType();

            foreach (var propertyName in propertyNames)
            {
                var prop = type.GetProperty(propertyName);
                if (prop == null)
                    continue;

                var value = prop.GetValue(obj) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static string NormalizeUserLookupKey(string? value)
        {
            var clean = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(clean))
                return "";

            clean = clean.Replace("/", "\\");

            var slashIndex = clean.LastIndexOf('\\');
            if (slashIndex >= 0 && slashIndex < clean.Length - 1)
                clean = clean[(slashIndex + 1)..];

            return clean.Trim();
        }

        private void AddCreatedByDisplayAlias(string? key, string? displayName)
        {
            var cleanDisplay = (displayName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleanDisplay))
                return;

            var cleanKey = (key ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleanKey))
                _createdByDisplayByUserId[cleanKey] = cleanDisplay;

            var normalizedKey = NormalizeUserLookupKey(key);
            if (!string.IsNullOrWhiteSpace(normalizedKey))
                _createdByDisplayByUserId[normalizedKey] = cleanDisplay;
        }

        private string ResolveCreatedByDisplay(string? createdBy)
        {
            if (string.IsNullOrWhiteSpace(createdBy))
                return "";

            var key = createdBy.Trim();

            if (_createdByDisplayByUserId.TryGetValue(key, out var display) &&
                !string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            var normalizedKey = NormalizeUserLookupKey(key);

            if (_createdByDisplayByUserId.TryGetValue(normalizedKey, out display) &&
                !string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            return key;
        }

        private string GetCurrentUserDisplayName()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("FULLNAME"),
                WindowsIdentity.GetCurrent()?.Name,
                Environment.UserName
            };

            foreach (var candidate in candidates)
            {
                var clean = (candidate ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(clean))
                    continue;

                var resolved = ResolveCreatedByDisplay(clean);

                if (!string.IsNullOrWhiteSpace(resolved) &&
                    !string.Equals(resolved, clean, StringComparison.OrdinalIgnoreCase))
                {
                    return resolved;
                }
            }

            var fullName = Environment.GetEnvironmentVariable("FULLNAME")?.Trim();
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName;

            var windowsName = WindowsIdentity.GetCurrent()?.Name;
            if (!string.IsNullOrWhiteSpace(windowsName))
                return NormalizeUserLookupKey(windowsName);

            return string.IsNullOrWhiteSpace(Environment.UserName)
                ? "Unknown"
                : Environment.UserName;
        }

        private async void AddSelectedTaskSiteNote_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditSelectedTaskTicket || SelectedTaskTicket == null)
                return;

            var site = SelectedTaskTicket.Site?.Trim();

            if (string.IsNullOrWhiteSpace(site))
                return;

            await LoadKnownTechAliasesAsync();

            var win = new SiteNoteEditorWindow(site)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            try
            {
                await _siteNotesApi.CreateAsync(new CreateSiteNoteRequest
                {
                    SiteId = site,
                    NoteType = "General",
                    NoteText = win.NoteText,
                    CreatedBy = GetCurrentUserDisplayName()
                });

                await LoadSiteNotesForSelectedTaskAsync(site);
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

        private async void EditSelectedTaskSiteNote_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditSelectedTaskTicket || SelectedTaskTicket == null)
                return;

            if (sender is not Button button || button.Tag is not SiteNoteDto note)
                return;

            var site = SelectedTaskTicket.Site?.Trim() ?? "";

            await LoadKnownTechAliasesAsync();

            var win = new SiteNoteEditorWindow(site, note)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            try
            {
                await _siteNotesApi.UpdateAsync(new UpdateSiteNoteRequest
                {
                    Id = note.Id,
                    NoteType = "General",
                    NoteText = win.NoteText,
                    UpdatedBy = GetCurrentUserDisplayName()
                });

                await LoadSiteNotesForSelectedTaskAsync(site);
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

        private async void DeleteSelectedTaskSiteNote_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditSelectedTaskTicket || SelectedTaskTicket == null)
                return;

            if (sender is not Button button || button.Tag is not SiteNoteDto note)
                return;

            var confirm = MessageBox.Show(
                "Delete this site note?",
                "Delete Site Note",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await LoadKnownTechAliasesAsync();
                await _siteNotesApi.DeleteAsync(note.Id, GetCurrentUserDisplayName());
                await LoadSiteNotesForSelectedTaskAsync(SelectedTaskTicket.Site);
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

        private void UpdateTaskSaveButtonState()
        {
            if (SaveTaskNotesButton != null)
            {
                SaveTaskNotesButton.IsEnabled =
                    CanEditSelectedTaskTicket &&
                    HasUnsavedSelectedTaskDispatchNotes;
            }

            UpdateTaskToolbarButtons();
        }

        private void TaskDispatchNotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTaskSaveButtonState();
        }

        private void TaskTechWriteUpsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTaskSaveButtonState();
        }

        private void CollapseTaskDetailExpanders()
        {
            if (TaskSiteNotesExpander != null)
                TaskSiteNotesExpander.IsExpanded = false;

            if (TaskDispatchNotesExpander != null)
                TaskDispatchNotesExpander.IsExpanded = false;

            if (TaskTechWriteUpsExpander != null)
                TaskTechWriteUpsExpander.IsExpanded = false;

            UpdateTaskDetailTextBoxHeights();
        }

        private void TaskDetailExpander_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(UpdateTaskDetailTextBoxHeights),
                DispatcherPriority.Background);
        }

        private void UpdateTaskDetailTextBoxHeights()
        {
            if (TaskDispatchNotesTextBox == null || TaskTechWriteUpsTextBox == null)
                return;

            var expandedCount = 0;

            if (TaskSiteNotesExpander?.IsExpanded == true)
                expandedCount++;

            if (TaskDispatchNotesExpander?.IsExpanded == true)
                expandedCount++;

            if (TaskTechWriteUpsExpander?.IsExpanded == true)
                expandedCount++;

            var dispatchHeight = expandedCount switch
            {
                0 => double.NaN,
                1 => 360,
                2 => 280,
                _ => 210
            };

            var techHeight = expandedCount switch
            {
                0 => double.NaN,
                1 => 430,
                2 => 320,
                _ => 240
            };

            TaskDispatchNotesTextBox.Height =
                TaskDispatchNotesExpander?.IsExpanded == true
                    ? dispatchHeight
                    : double.NaN;

            TaskTechWriteUpsTextBox.Height =
                TaskTechWriteUpsExpander?.IsExpanded == true
                    ? techHeight
                    : double.NaN;
        }

        private async void SaveTaskNotes_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTaskTicket == null || IsSelectedTaskTicketClosed)
                return;

            var ticket = SelectedTaskTicket;

            var req = new UpdateTicketRequest(
                Site: ticket.Site ?? "",
                NotificationName: ticket.NotificationName ?? "",
                Notification: ticket.Notification ?? "",
                WorkOrder: string.IsNullOrWhiteSpace(ticket.CurrentWorkOrder) ? null : ticket.CurrentWorkOrder,
                WorkOrderClass: ticket.WorkOrderType ?? "",
                GroupCode: ticket.GroupCode ?? "",
                PriorityDays: ticket.PriorityDays,
                Status: ticket.Status ?? "",
                TaskCategoryId: ticket.TaskCategoryId,
                ActionRequiredOverride: string.IsNullOrWhiteSpace(ticket.ActionRequiredOverride)
                    ? null
                    : ticket.ActionRequiredOverride,
                AssignedTech: ticket.AssignedTech ?? "(Unassigned)",
                Problem: ticket.Problem ?? "",
                Notes: ticket.Notes ?? "",
                DispatchNotes: ticket.DispatchNotes ?? ""
            );

            SaveTaskNotesButton.IsEnabled = false;

            try
            {
                await _ticketsApi.UpdateTicketAsync(ticket.Id, req);

                _selectedTaskOriginalDispatchNotes = ticket.DispatchNotes ?? "";
                _selectedTaskOriginalTechNotes = ticket.Notes ?? "";

                UpdateTaskSaveButtonState();

                MessageBox.Show(
                    "Dispatch Notes / Tech Write-Ups saved.",
                    "Task Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to save notes.\n\n{ex.Message}",
                    "Task Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                UpdateTaskSaveButtonState();
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

            UpdateTaskToolbarButtons();
            UpdateTaskSaveButtonState();
        }

        private void SetTaskPaneControlsEnabled(bool enabled)
        {
            SearchBox.IsEnabled = enabled;
            StatusFilter.IsEnabled = enabled;
            ClearTaskFiltersButton.IsEnabled = enabled;
            RefreshTasksButton.IsEnabled = enabled;
            TasksGrid.IsEnabled = enabled;

            DetailsPanel.IsEnabled = enabled;

            OpenTaskDetailsButton.IsEnabled = enabled && SelectedTask != null;
            OpenTaskTicketButton.IsEnabled = enabled && SelectedTask != null;

            var canCloseSelectedTask =
                enabled &&
                SelectedTask != null &&
                !HasUnsavedSelectedTaskDispatchNotes &&
                !IsSelectedTaskTicketClosed;

            MarkTaskClosedButton.IsEnabled = canCloseSelectedTask;
            MarkDetailsClosedButton.IsEnabled = canCloseSelectedTask;

            SaveTaskNotesButton.IsEnabled =
                enabled &&
                CanEditSelectedTaskTicket &&
                HasUnsavedSelectedTaskDispatchNotes;
        }
    }
}