#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Dispatcher.DailyAssignments;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using SmartGridSuite.Contracts.Tickets;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class DailyAssignmentsPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private sealed class TicketPoolDragPayload
        {
            public List<long> TicketIds { get; init; } = new();
        }

        private readonly ApiClient _api = ClientAppSettings.CreateApiClient();
        private readonly TicketsApi _ticketsApi = new(ClientAppSettings.CreateApiClient());
        private readonly DispatcherTimer _ticketSearchTimer;
        private readonly ObservableCollection<DailyAssignmentTicketDto> _filteredTicketPool = new();
        private readonly ObservableCollection<AssignmentTargetVm> _assignmentTargets = new();
        private readonly HashSet<long> _selectedTicketIds = new();

        private Point _assignedTicketDragStartPoint;
        private DailyAssignedTicketDto? _draggedAssignedTicket;
        private bool _isReorderingByDragDrop;

        private Point _ticketPoolDragStartPoint;
        private DailyAssignmentTicketDto? _draggedPoolTicket;
        private readonly List<long> _ticketPoolDragTicketIds = new();
        private bool _isAssigningByDragDrop;

        private int? _routePreviewIndex;
        private bool _routePreviewFromPool;
        private DailyAssignedTicketDto? _routePreviewDraggedTicket;
        private ListBoxItem? _dimmedDraggedRouteItem;

        private Popup? _dragGhostPopup;
        private ContentPresenter? _dragGhostPresenter;

        private bool _hasLoaded;
        private bool _busyLoading;
        private bool _syncingTicketSelection;
        private bool _includeAssignedTickets;

        private string _statusText = "Ready.";
        private string _ticketSearchText = "";

        private int _busyOverlayDepth;

        private AssignmentTargetVm? _selectedTarget;

        private DailyAssignmentsBoardDto _board = new()
        {
            WorkDate = DateTime.Today
        };

        public DailyAssignmentsBoardDto Board
        {
            get => _board;
            private set
            {
                var previousTargetKey = SelectedTarget?.TargetKey;

                _board = value ?? new DailyAssignmentsBoardDto
                {
                    WorkDate = DateTime.Today
                };

                NormalizeBoardForDisplay(_board);
                RebuildAssignmentTargets(previousTargetKey);
                ApplyTicketPoolFilter();
                RefreshAllBindings();
            }
        }

        public ObservableCollection<DailyAssignmentTicketDto> FilteredTicketPool => _filteredTicketPool;
        public ObservableCollection<AssignmentTargetVm> AssignmentTargets => _assignmentTargets;

        public AssignmentTargetVm? SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                if (ReferenceEquals(_selectedTarget, value))
                    return;

                _selectedTarget = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedTarget));
                OnPropertyChanged(nameof(HasSelectedTargetAssignedTickets));
                OnPropertyChanged(nameof(SelectedTargetSubtitle));
                OnPropertyChanged(nameof(SelectedTargetPublishStatusText));
                OnPropertyChanged(nameof(SelectedTargetHeaderText));
                OnPropertyChanged(nameof(SelectedTargetContextText));
                OnPropertyChanged(nameof(SelectedTargetHeaderText));
                OnPropertyChanged(nameof(SelectedTargetContextText));

                /*
                 * The selected route determines which tickets must be excluded from
                 * the Ticket Pool. Refresh immediately when the dispatcher switches
                 * crews or technicians.
                 */
                ApplyTicketPoolFilter();
            }
        }

        public bool HasSelectedTarget => SelectedTarget != null;

        public bool HasSelectedTargetAssignedTickets => SelectedTarget?.AssignedTicketCount > 0;

        public string HeaderSubtitle =>
            $"Assign tickets for {Board.WorkDate:dddd, MMMM d, yyyy}.";

        public string SelectedTargetSubtitle =>
            SelectedTarget == null
                ? "Choose a crew or individual technician."
                : $"{SelectedTarget.PrimaryText} · {SelectedTarget.SecondaryText}";

        public string SelectedTargetHeaderText =>
            SelectedTarget?.PrimaryText
            ?? "No Crew / Technician Selected";

        public string SelectedTargetContextText
        {
            get
            {
                var target = SelectedTarget;

                if (target == null)
                    return "Select a crew or technician above.";

                var targetLabel =
                    target.TargetType.Equals(
                        "Truck",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Crew Work List"
                        : "Technician Work List";

                var ticketText =
                    target.AssignedTicketCount == 1
                        ? "1 ticket"
                        : $"{target.AssignedTicketCount} tickets";

                if (string.IsNullOrWhiteSpace(target.SecondaryText))
                    return $"{targetLabel} · {ticketText}";

                return
                    $"{targetLabel} · {target.SecondaryText} · {ticketText}";
            }
        }

        public string SelectedTargetPublishStatusText => SelectedTarget?.PublishStatusText ?? "";

        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public int TicketPoolCount => Board.TicketPool.Count;

        public int VisibleTicketPoolCount => FilteredTicketPool.Count;

        public int SelectedTicketCount => _selectedTicketIds.Count;

        public bool HasSelectedTickets => _selectedTicketIds.Count > 0;

        public string TicketPoolSummaryText =>
            $"Showing {VisibleTicketPoolCount} of {TicketPoolCount} tickets · {SelectedTicketCount} selected";

        public int AssignedTicketCount =>
            Board.TruckTargets.Sum(x => x.AssignedTickets.Count) +
            Board.TechnicianTargets.Sum(x => x.AssignedTickets.Count);

        public int FieldCompleteCount =>
            Board.TicketPool.Count(x => x.IsFieldComplete) +
            Board.TruckTargets.Sum(t => t.AssignedTickets.Count(x => x.IsFieldComplete)) +
            Board.TechnicianTargets.Sum(t => t.AssignedTickets.Count(x => x.IsFieldComplete));

        public string BoardStatusText =>
            Board.PublishedVersion <= 0
                ? "Draft"
                : $"v{Board.PublishedVersion}";

        public DailyAssignmentsPaneView()
        {
            InitializeComponent();
            DataContext = this;

            _ticketSearchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            _ticketSearchTimer.Tick += (_, __) =>
            {
                _ticketSearchTimer.Stop();
                ApplyTicketPoolFilter();
            };

            Loaded += async (_, __) =>
            {
                if (_hasLoaded)
                    return;

                _hasLoaded = true;
                await LoadBoardAsync();
            };
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadBoardAsync();
        }

        private async void NewTicket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var techSuggestions =
                    Board.TechnicianTargets
                        .Select(x => x.TechnicianName)
                        .Concat(
                            Board.TruckTargets
                                .SelectMany(x => x.Technicians)
                                .Select(x => x.Name))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(
                            x => x,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var win = new NewTicketWindow(
                    _ticketsApi,
                    techSuggestions)
                {
                    Owner = Window.GetWindow(this)
                };

                if (win.ShowDialog() != true)
                    return;

                await LoadBoardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "New Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task LoadBoardAsync()
        {
            if (_busyLoading)
                return;

            try
            {
                _busyLoading = true;

                StatusText = "Loading daily assignments...";
                ShowBusyOverlay(StatusText);

                var date = DateTime.Today.ToString("yyyy-MM-dd");

                var dto = await _api.GetAsync<DailyAssignmentsBoardDto>(
                    $"api/daily-assignments/board?date={date}");

                Board = dto ?? new DailyAssignmentsBoardDto
                {
                    WorkDate = DateTime.Today
                };

                StatusText =
                    $"Loaded {TicketPoolCount} pool ticket(s), {AssignedTicketCount} assigned ticket(s), " +
                    $"{AssignmentTargets.Count} crew/tech target(s).";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"API error: {ex.Body ?? ex.Message}";
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Daily Assignments Load Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Load failed: " + ex.Message;
                MessageBox.Show(
                    ex.Message,
                    "Daily Assignments Load Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _busyLoading = false;
                HideBusyOverlay();
            }
        }

        private void TicketPoolSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ticketSearchText = TicketPoolSearchBox.Text?.Trim() ?? "";

            _ticketSearchTimer.Stop();
            _ticketSearchTimer.Start();
        }

        private void IncludeAssignedTickets_Changed(object sender, RoutedEventArgs e)
        {
            _includeAssignedTickets = IncludeAssignedTicketsCheckBox.IsChecked == true;
            ApplyTicketPoolFilter();
        }

        private void ApplyTicketPoolFilter()
        {
            var q =
                (_ticketSearchText ?? string.Empty).Trim();

            IEnumerable<DailyAssignmentTicketDto> filtered =
                Board.TicketPool;

            /*
             * A ticket already present in the selected route must never remain in the
             * pool for that same target. This prevents duplicate assignment attempts
             * and makes drag/drop behavior clear.
             */
            var selectedTargetTicketIds =
                SelectedTarget?.AssignedTickets
                    .Select(x => x.TicketId)
                    .Where(x => x > 0)
                    .ToHashSet()
                ?? new HashSet<long>();

            if (selectedTargetTicketIds.Count > 0)
            {
                filtered = filtered.Where(
                    ticket =>
                        !selectedTargetTicketIds.Contains(
                            ticket.TicketId));
            }

            /*
             * By default, also hide tickets assigned to any other target. When
             * "Include assigned tickets" is enabled, tickets assigned elsewhere may
             * appear, but tickets already in the selected route remain excluded.
             */
            if (!_includeAssignedTickets)
            {
                filtered = filtered.Where(
                    ticket =>
                        ticket.CurrentAssignmentId == null);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                bool Match(string? value) =>
                    !string.IsNullOrWhiteSpace(value) &&
                    value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                filtered = filtered.Where(t =>
                    Match(t.Site) ||
                    Match(t.NotificationName) ||
                    Match(t.Notification) ||
                    Match(t.Status) ||
                    Match(t.AssignedTech) ||
                    Match(t.WorkOrder) ||
                    Match(t.WorkOrderClass) ||
                    Match(t.GroupCode) ||
                    Match(t.Problem) ||
                    Match(t.Notes) ||
                    Match(t.DispatchNotes) ||
                    Match(t.TaskCategoryName) ||
                    Match(t.ActionRequiredOverride));
            }

            var filteredList = filtered
                .OrderBy(t => t.PriorityDays == 0 ? 999 : t.PriorityDays)
                .ThenByDescending(t => t.LastActivityAt)
                .ToList();

            _syncingTicketSelection = true;

            try
            {
                _filteredTicketPool.Clear();

                foreach (var ticket in filteredList)
                    _filteredTicketPool.Add(ticket);

                var visibleIds = filteredList
                    .Select(x => x.TicketId)
                    .ToHashSet();

                _selectedTicketIds.RemoveWhere(id => !visibleIds.Contains(id));
            }
            finally
            {
                _syncingTicketSelection = false;
            }

            OnPropertyChanged(nameof(FilteredTicketPool));
            OnPropertyChanged(nameof(VisibleTicketPoolCount));
            OnPropertyChanged(nameof(SelectedTicketCount));
            OnPropertyChanged(nameof(HasSelectedTickets));
            OnPropertyChanged(nameof(TicketPoolSummaryText));
        }

        private void TicketPoolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTicketSelection)
                return;

            _selectedTicketIds.Clear();

            if (TicketPoolList?.SelectedItems != null)
            {
                foreach (var ticket in TicketPoolList.SelectedItems.OfType<DailyAssignmentTicketDto>())
                    _selectedTicketIds.Add(ticket.TicketId);
            }

            OnPropertyChanged(nameof(SelectedTicketCount));
            OnPropertyChanged(nameof(HasSelectedTickets));
            OnPropertyChanged(nameof(TicketPoolSummaryText));
        }

        private List<long> GetSelectedTicketIds()
        {
            return _selectedTicketIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }

        private void ClearSelectedTickets()
        {
            _selectedTicketIds.Clear();

            if (TicketPoolList != null)
            {
                _syncingTicketSelection = true;

                try
                {
                    TicketPoolList.SelectedItems.Clear();
                }
                finally
                {
                    _syncingTicketSelection = false;
                }
            }

            OnPropertyChanged(nameof(SelectedTicketCount));
            OnPropertyChanged(nameof(HasSelectedTickets));
            OnPropertyChanged(nameof(TicketPoolSummaryText));
        }

        private async void AddSelectedToSelectedTarget_Click(object sender, RoutedEventArgs e)
        {
            var target = SelectedTarget;

            if (target == null)
            {
                MessageBox.Show(
                    "Select a crew or technician first.",
                    "Add Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var ticketIds = GetSelectedTicketIds();

            if (ticketIds.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more tickets from the Ticket Pool first.",
                    "Add Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ShowBusyOverlay("Assigning selected ticket(s)...");

            try
            {
                StatusText = "Assigning selected ticket(s)...";

                var req = new AssignDailyTicketsRequest
                {
                    WorkDate = Board.WorkDate,
                    TicketIds = ticketIds,
                    TargetType = target.TargetType,
                    TruckId = target.TruckId,
                    TechnicianId = target.TechnicianId,
                    AssignmentNotes = null,
                    UpdatedBy = Environment.UserName
                };

                var result = await AssignTicketsWithConflictWarningAsync(
                    req,
                    target.PrimaryText);

                if (result == null)
                    return;

                ClearSelectedTickets();

                await LoadBoardAsync();

                StatusText = $"Assigned {result?.AssignedCount ?? ticketIds.Count} ticket(s) to {target.PrimaryText}.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Assign failed: {ex.Body ?? ex.Message}";
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Assign Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Assign failed: " + ex.Message;
                MessageBox.Show(
                    ex.Message,
                    "Assign Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private async void RemoveAssignedTicket_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DailyAssignedTicketDto ticket)
                return;

            await RemoveAssignedTicketsAsync(
                new List<long> { ticket.TicketId },
                "Removing assigned ticket...",
                $"Removed {ticket.Site} from the selected list.");
        }

        private async Task RemoveAssignedTicketsAsync(List<long> ticketIds, string workingStatus, string successStatus)
        {
            if (ticketIds.Count == 0)
                return;

            ShowBusyOverlay(workingStatus);

            try
            {
                StatusText = workingStatus;

                var req = new RemoveDailyTicketAssignmentsRequest
                {
                    WorkDate = Board.WorkDate,
                    TicketIds = ticketIds,
                    UpdatedBy = Environment.UserName
                };

                var result = await _api.PostAsync<RemoveDailyTicketAssignmentsRequest, RemoveDailyTicketAssignmentsResponse>(
                    "api/daily-assignments/remove",
                    req);

                await LoadBoardAsync();

                StatusText = successStatus;
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Remove failed: {ex.Body ?? ex.Message}";
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Remove Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Remove failed: " + ex.Message;
                MessageBox.Show(
                    ex.Message,
                    "Remove Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private void TicketPoolList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _ticketPoolDragStartPoint = e.GetPosition(null);
            _draggedPoolTicket = null;
            _ticketPoolDragTicketIds.Clear();

            // Checkbox clicks are selection actions, not drag handles.
            if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) != null)
                return;

            var row = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

            if (row?.DataContext is not DailyAssignmentTicketDto ticket)
                return;

            _draggedPoolTicket = ticket;

            /*
             * Snapshot the drag payload before WPF changes selection as a result
             * of pressing the mouse button on a selected card.
             *
             * Dragging any currently selected card carries the entire current selection.
             * Dragging an unselected card carries only that card.
             */
            if (_selectedTicketIds.Contains(ticket.TicketId))
            {
                _ticketPoolDragTicketIds.AddRange(
                    FilteredTicketPool
                        .Where(x => _selectedTicketIds.Contains(x.TicketId))
                        .Select(x => x.TicketId)
                        .Where(x => x > 0)
                        .Distinct());
            }
            else
            {
                _ticketPoolDragTicketIds.Add(ticket.TicketId);
            }
        }

        private async void TicketPoolList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            // Double-clicking the checkbox should only affect selection.
            if (FindVisualParent<CheckBox>(
                    e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            // Route action buttons should only perform their own action.
            if (FindVisualParent<Button>(
                    e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            var item = FindVisualParent<ListBoxItem>(
                e.OriginalSource as DependencyObject);

            if (item?.DataContext is not DailyAssignmentTicketDto ticket ||
                ticket.TicketId <= 0)
            {
                return;
            }

            try
            {
                StatusText = $"Opening ticket for {ticket.Site}...";

                // Always load the current ticket before opening Edit Ticket.
                var dto = await _ticketsApi.GetTicketByIdAsync(
                    ticket.TicketId);

                if (dto == null)
                {
                    MessageBox.Show(
                        "The ticket could not be loaded.",
                        "Edit Ticket",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    StatusText = "Ticket could not be loaded.";
                    return;
                }

                var ticketToEdit = MapTicketForEditor(dto);

                var techSuggestions =
                    Board.TechnicianTargets
                        .Select(x => x.TechnicianName)
                        .Concat(
                            Board.TruckTargets
                                .SelectMany(x => x.Technicians)
                                .Select(x => x.Name))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(
                            x => x,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var win = new NewTicketWindow(
                    _ticketsApi,
                    techSuggestions,
                    ticketToEdit)
                {
                    Owner = Window.GetWindow(this)
                };

                var result = win.ShowDialog();

                if (result == true || win.WasDeleted)
                {
                    await LoadBoardAsync();

                    StatusText = win.WasDeleted
                        ? $"Ticket for {ticket.Site} deleted."
                        : $"Ticket for {ticket.Site} updated.";
                }
                else
                {
                    StatusText = "Ready.";
                }
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText =
                    $"Ticket load failed: {ex.Body ?? ex.Message}";

                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Edit Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText =
                    "Ticket load failed: " + ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "Edit Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static DispatchTicket MapTicketForEditor(TicketListItemDto dto)
        {
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
                WorkOrderType = dto.WorkOrderClass ?? "",
                GroupCode = dto.GroupCode,
                PriorityDays = dto.PriorityDays,
                Problem = dto.Problem,
                Notes = dto.Notes,
                DispatchNotes = dto.DispatchNotes,
                CreatedBy = dto.CreatedBy,
                TaskCategoryId = dto.TaskCategoryId,
                TaskCategoryName = dto.TaskCategoryName ?? "",
                ActionRequiredOverride =
                    dto.ActionRequiredOverride ?? ""
            };
        }

        private void TicketPoolList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed ||
                _draggedPoolTicket == null ||
                _isAssigningByDragDrop ||
                _isReorderingByDragDrop ||
                SelectedTarget == null)
            {
                return;
            }

            if (FindVisualParent<CheckBox>(e.OriginalSource as DependencyObject) != null)
                return;

            var currentPosition = e.GetPosition(null);

            var movedEnough =
                Math.Abs(currentPosition.X - _ticketPoolDragStartPoint.X) >
                    SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPosition.Y - _ticketPoolDragStartPoint.Y) >
                    SystemParameters.MinimumVerticalDragDistance;

            if (!movedEnough)
                return;

            var ticketIds = _ticketPoolDragTicketIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return;

            var payload = new TicketPoolDragPayload
            {
                TicketIds = ticketIds
            };

            BeginDragGhost(_draggedPoolTicket, ticketIds.Count);

            try
            {
                DragDrop.DoDragDrop(
                    TicketPoolList,
                    payload,
                    DragDropEffects.Move);
            }
            finally
            {
                ResetRouteCardPreview();
                EndDragGhost();
                _draggedPoolTicket = null;
                _ticketPoolDragTicketIds.Clear();
            }
        }

        private void AssignedTicketsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _assignedTicketDragStartPoint = e.GetPosition(null);
            _draggedAssignedTicket = null;

            if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            var row = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);

            if (row?.DataContext is DailyAssignedTicketDto ticket)
                _draggedAssignedTicket = ticket;
        }

        private void AssignedTicketsList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed ||
                _draggedAssignedTicket == null ||
                _isReorderingByDragDrop ||
                _isAssigningByDragDrop)
            {
                return;
            }

            if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            var currentPosition = e.GetPosition(null);

            var movedEnough =
                Math.Abs(currentPosition.X - _assignedTicketDragStartPoint.X) >
                    SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPosition.Y - _assignedTicketDragStartPoint.Y) >
                    SystemParameters.MinimumVerticalDragDistance;

            if (!movedEnough)
                return;

            BeginDraggedRouteCardVisual(_draggedAssignedTicket);
            BeginDragGhost(_draggedAssignedTicket, 1);

            try
            {
                DragDrop.DoDragDrop(
                    AssignedTicketsList,
                    _draggedAssignedTicket,
                    DragDropEffects.Move);
            }
            finally
            {
                ResetRouteCardPreview();
                EndDragGhost();
                _draggedAssignedTicket = null;
            }
        }

        private void AssignedTicketsList_DragOver(object sender, DragEventArgs e)
        {
            var target = SelectedTarget;

            if (_isReorderingByDragDrop ||
                _isAssigningByDragDrop ||
                target == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(typeof(TicketPoolDragPayload)))
            {
                var payload = e.Data.GetData(typeof(TicketPoolDragPayload)) as TicketPoolDragPayload;

                if (payload == null || payload.TicketIds.Count == 0)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                var insertionIndex = GetRouteInsertionIndex(e);

                ShowPoolDropGapPreview(
                    insertionIndex,
                    payload.TicketIds.Distinct().Count());

                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(typeof(DailyAssignedTicketDto)))
            {
                var draggedTicket = e.Data.GetData(typeof(DailyAssignedTicketDto)) as DailyAssignedTicketDto;

                if (draggedTicket == null)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                var destinationIndex = GetAssignedRouteDestinationIndex(e, draggedTicket);

                ShowAssignedMoveGapPreview(
                    draggedTicket,
                    destinationIndex);

                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void AssignedTicketsList_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is not ListBox list)
                return;

            var position = e.GetPosition(list);

            const double tolerance = 3;

            var isActuallyOutside =
                position.X < -tolerance ||
                position.Y < -tolerance ||
                position.X > list.ActualWidth + tolerance ||
                position.Y > list.ActualHeight + tolerance;

            if (isActuallyOutside)
                ResetRouteCardPreview();
        }

        private async void AssignedTicketsList_Drop(object sender, DragEventArgs e)
        {
            var target = SelectedTarget;

            if (_isReorderingByDragDrop ||
                _isAssigningByDragDrop ||
                target == null)
            {
                ResetRouteCardPreview();
                return;
            }

            if (e.Data.GetDataPresent(typeof(TicketPoolDragPayload)))
            {
                var payload = e.Data.GetData(typeof(TicketPoolDragPayload)) as TicketPoolDragPayload;

                if (payload == null || payload.TicketIds.Count == 0)
                {
                    ResetRouteCardPreview();
                    return;
                }

                var insertionIndex =
                    _routePreviewFromPool && _routePreviewIndex.HasValue
                        ? _routePreviewIndex.Value
                        : GetRouteInsertionIndex(e);

                long? insertBeforeTicketId =
                    insertionIndex >= 0 && insertionIndex < target.AssignedTickets.Count
                        ? target.AssignedTickets[insertionIndex].TicketId
                        : null;

                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                ResetRouteCardPreview();

                await AssignDroppedPoolTicketsAsync(
                    payload.TicketIds,
                    insertBeforeTicketId);

                return;
            }

            if (!e.Data.GetDataPresent(typeof(DailyAssignedTicketDto)))
            {
                ResetRouteCardPreview();
                return;
            }

            var draggedTicket = e.Data.GetData(typeof(DailyAssignedTicketDto)) as DailyAssignedTicketDto;

            if (draggedTicket == null)
            {
                ResetRouteCardPreview();
                return;
            }

            var originalTickets = target.AssignedTickets.ToList();
            var originalIndex = originalTickets.IndexOf(draggedTicket);

            var destinationIndex =
                !_routePreviewFromPool &&
                ReferenceEquals(_routePreviewDraggedTicket, draggedTicket) &&
                _routePreviewIndex.HasValue
                    ? _routePreviewIndex.Value
                    : GetAssignedRouteDestinationIndex(e, draggedTicket);

            if (originalIndex < 0 ||
                destinationIndex < 0 ||
                destinationIndex == originalIndex)
            {
                ResetRouteCardPreview();
                return;
            }

            originalTickets.RemoveAt(originalIndex);
            destinationIndex = Math.Clamp(destinationIndex, 0, originalTickets.Count);
            originalTickets.Insert(destinationIndex, draggedTicket);

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            ResetRouteCardPreview();

            await SaveDraggedAssignmentOrderAsync(originalTickets);
        }

        private void BeginDragGhost(DailyAssignmentTicketDto ticket, int ticketCount)
        {
            EndDragGhost();

            var template = TryFindResource("DragGhostTicketTemplate") as DataTemplate;

            if (template == null)
                return;

            var content = new TicketDragGhostVm
            {
                Ticket = ticket,
                AdditionalCount = Math.Max(0, ticketCount - 1)
            };

            _dragGhostPresenter = new ContentPresenter
            {
                Content = content,
                ContentTemplate = template,
                IsHitTestVisible = false,
                Opacity = 0.94
            };

            _dragGhostPopup = new Popup
            {
                AllowsTransparency = true,
                IsHitTestVisible = false,
                StaysOpen = true,
                Placement = PlacementMode.AbsolutePoint,
                Child = _dragGhostPresenter
            };

            _dragGhostPopup.IsOpen = true;

            UpdateDragGhostPosition();
        }

        private void DragSource_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            UpdateDragGhostPosition();

            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        private void UpdateDragGhostPosition()
        {
            if (_dragGhostPopup == null)
                return;

            if (!GetCursorPos(out var cursorPosition))
                return;

            var screenPoint = new Point(cursorPosition.X, cursorPosition.Y);

            /*
             * GetCursorPos returns device pixels. WPF Popup offsets use
             * device-independent units, so convert before positioning.
             */
            var source = PresentationSource.FromVisual(this);

            if (source?.CompositionTarget != null)
            {
                screenPoint = source.CompositionTarget
                    .TransformFromDevice
                    .Transform(screenPoint);
            }

            _dragGhostPopup.HorizontalOffset = screenPoint.X + 18;
            _dragGhostPopup.VerticalOffset = screenPoint.Y + 18;
        }

        private void EndDragGhost()
        {
            if (_dragGhostPopup != null)
            {
                _dragGhostPopup.IsOpen = false;
                _dragGhostPopup.Child = null;
            }

            _dragGhostPresenter = null;
            _dragGhostPopup = null;
        }

        private void BeginDraggedRouteCardVisual(DailyAssignedTicketDto ticket)
        {
            ResetRouteCardPreview();

            _routePreviewDraggedTicket = ticket;

            _dimmedDraggedRouteItem =
                AssignedTicketsList.ItemContainerGenerator.ContainerFromItem(ticket) as ListBoxItem;

            if (_dimmedDraggedRouteItem != null)
                _dimmedDraggedRouteItem.Opacity = 0.20;
        }

        private int GetRouteInsertionIndex(DragEventArgs e)
        {
            var target = SelectedTarget;

            if (target == null)
                return 0;

            var pointerPosition = e.GetPosition(AssignedTicketsList);

            for (var index = 0; index < target.AssignedTickets.Count; index++)
            {
                var ticket = target.AssignedTickets[index];

                if (AssignedTicketsList.ItemContainerGenerator.ContainerFromItem(ticket) is not ListBoxItem item ||
                    item.ActualHeight <= 0)
                {
                    continue;
                }

                /*
                 * TranslatePoint includes the visual animation offset.
                 * Subtract that offset so the drop calculation uses the card's
                 * stable original position instead of chasing the animation.
                 */
                var renderedTop = item
                    .TranslatePoint(new Point(0, 0), AssignedTicketsList)
                    .Y;

                var animatedOffset = item.RenderTransform is TranslateTransform transform
                    ? transform.Y
                    : 0;

                var stableTop = renderedTop - animatedOffset;
                var stableMiddle = stableTop + item.ActualHeight / 2;

                if (pointerPosition.Y < stableMiddle)
                    return index;
            }

            return target.AssignedTickets.Count;
        }

        private int GetAssignedRouteDestinationIndex(DragEventArgs e, DailyAssignedTicketDto draggedTicket)
        {
            var target = SelectedTarget;

            if (target == null)
                return -1;

            var sourceIndex = target.AssignedTickets.IndexOf(draggedTicket);

            if (sourceIndex < 0)
                return -1;

            var insertionIndex = GetRouteInsertionIndex(e);

            if (insertionIndex > sourceIndex)
                insertionIndex--;

            return Math.Clamp(
                insertionIndex,
                0,
                target.AssignedTickets.Count - 1);
        }

        private void ShowPoolDropGapPreview(int insertionIndex, int ticketCount)
        {
            var target = SelectedTarget;

            if (target == null)
                return;

            var safeInsertionIndex = Math.Clamp(
                insertionIndex,
                0,
                target.AssignedTickets.Count);

            /*
             * DragOver fires constantly. If the intended position has not changed,
             * do not restart every card animation.
             */
            if (_routePreviewFromPool &&
                _routePreviewIndex == safeInsertionIndex)
            {
                return;
            }

            _routePreviewFromPool = true;
            _routePreviewIndex = safeInsertionIndex;
            _routePreviewDraggedTicket = null;

            var shiftDistance =
                GetRouteCardShiftDistance() * Math.Max(1, ticketCount);

            for (var index = 0; index < target.AssignedTickets.Count; index++)
            {
                var offset = index >= safeInsertionIndex
                    ? shiftDistance
                    : 0;

                AnimateRouteCardOffset(
                    target.AssignedTickets[index],
                    offset);
            }
        }

        private void ShowAssignedMoveGapPreview(DailyAssignedTicketDto draggedTicket, int destinationIndex)
        {
            var target = SelectedTarget;

            if (target == null)
                return;

            var sourceIndex = target.AssignedTickets.IndexOf(draggedTicket);

            if (sourceIndex < 0 || destinationIndex < 0)
                return;

            var safeDestinationIndex = Math.Clamp(
                destinationIndex,
                0,
                Math.Max(0, target.AssignedTickets.Count - 1));

            /*
             * Prevent DragOver from restarting identical animations repeatedly.
             */
            if (!_routePreviewFromPool &&
                ReferenceEquals(_routePreviewDraggedTicket, draggedTicket) &&
                _routePreviewIndex == safeDestinationIndex)
            {
                return;
            }

            _routePreviewFromPool = false;
            _routePreviewIndex = safeDestinationIndex;
            _routePreviewDraggedTicket = draggedTicket;

            if (_dimmedDraggedRouteItem == null)
            {
                _dimmedDraggedRouteItem =
                    AssignedTicketsList.ItemContainerGenerator.ContainerFromItem(draggedTicket) as ListBoxItem;

                if (_dimmedDraggedRouteItem != null)
                    _dimmedDraggedRouteItem.Opacity = 0.20;
            }

            var shiftDistance = GetRouteCardShiftDistance(draggedTicket);

            for (var index = 0; index < target.AssignedTickets.Count; index++)
            {
                var ticket = target.AssignedTickets[index];

                double offset = 0;

                if (!ReferenceEquals(ticket, draggedTicket))
                {
                    if (safeDestinationIndex < sourceIndex &&
                        index >= safeDestinationIndex &&
                        index < sourceIndex)
                    {
                        offset = shiftDistance;
                    }
                    else if (safeDestinationIndex > sourceIndex &&
                             index > sourceIndex &&
                             index <= safeDestinationIndex)
                    {
                        offset = -shiftDistance;
                    }
                }

                AnimateRouteCardOffset(ticket, offset);
            }
        }

        private double GetRouteCardShiftDistance(DailyAssignedTicketDto? preferredTicket = null)
        {
            if (preferredTicket != null &&
                AssignedTicketsList.ItemContainerGenerator.ContainerFromItem(preferredTicket) is ListBoxItem preferredItem &&
                preferredItem.ActualHeight > 0)
            {
                return preferredItem.ActualHeight;
            }

            if (SelectedTarget != null)
            {
                foreach (var ticket in SelectedTarget.AssignedTickets)
                {
                    if (AssignedTicketsList.ItemContainerGenerator.ContainerFromItem(ticket) is ListBoxItem item &&
                        item.ActualHeight > 0)
                    {
                        return item.ActualHeight;
                    }
                }
            }

            return 112;
        }

        private void AnimateRouteCardOffset(DailyAssignedTicketDto ticket, double targetOffset, bool animate = true)
        {
            if (AssignedTicketsList.ItemContainerGenerator.ContainerFromItem(ticket) is not ListBoxItem item)
                return;

            if (item.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                item.RenderTransform = transform;
            }

            var currentOffset = transform.Y;

            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = currentOffset;

            if (!animate)
            {
                transform.Y = targetOffset;
                return;
            }

            var animation = new DoubleAnimation
            {
                From = currentOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(115),
                FillBehavior = FillBehavior.HoldEnd,
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            transform.BeginAnimation(
                TranslateTransform.YProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void ResetRouteCardPreview()
        {
            var hadActivePreview =
                _routePreviewIndex.HasValue ||
                _dimmedDraggedRouteItem != null;

            if (hadActivePreview && SelectedTarget != null)
            {
                foreach (var ticket in SelectedTarget.AssignedTickets)
                    AnimateRouteCardOffset(ticket, 0);
            }

            if (_dimmedDraggedRouteItem != null)
                _dimmedDraggedRouteItem.ClearValue(OpacityProperty);

            _dimmedDraggedRouteItem = null;
            _routePreviewIndex = null;
            _routePreviewFromPool = false;
            _routePreviewDraggedTicket = null;
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
            SetDailyAssignmentsControlsEnabled(false);
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
            SetDailyAssignmentsControlsEnabled(true);

            RefreshAllBindings();
        }

        private void SetDailyAssignmentsControlsEnabled(bool enabled)
        {
            RefreshAssignmentsButton.IsEnabled = enabled;

            TicketPoolCard.IsEnabled = enabled;
            WorkListCard.IsEnabled = enabled;

            if (!enabled)
            {
                EndDragGhost();
                ResetRouteCardPreview();
            }
        }

        private async Task AssignDroppedPoolTicketsAsync(IReadOnlyList<long> ticketIds, long? insertBeforeTicketId)
        {
            var target = SelectedTarget;

            if (target == null)
                return;

            var cleanTicketIds = ticketIds
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (cleanTicketIds.Count == 0)
                return;

            var targetKey = target.TargetKey;

            ShowBusyOverlay("Assigning dropped ticket(s)...");

            try
            {
                _isAssigningByDragDrop = true;

                StatusText = cleanTicketIds.Count == 1
                    ? $"Adding ticket to {target.PrimaryText}..."
                    : $"Adding {cleanTicketIds.Count} tickets to {target.PrimaryText}...";

                var request = new AssignDailyTicketsRequest
                {
                    WorkDate = Board.WorkDate,
                    TicketIds = cleanTicketIds,
                    TargetType = target.TargetType,
                    TruckId = target.TruckId,
                    TechnicianId = target.TechnicianId,
                    AssignmentNotes = null,
                    UpdatedBy = Environment.UserName
                };

                var assignResult = await AssignTicketsWithConflictWarningAsync(
                    request,
                    target.PrimaryText);

                if (assignResult == null)
                    return;

                ClearSelectedTickets();

                await LoadBoardAsync();

                var refreshedTarget = AssignmentTargets
                    .FirstOrDefault(x => x.TargetKey == targetKey);

                if (refreshedTarget == null)
                {
                    StatusText = "Tickets were assigned, but the selected crew/technician could not be restored.";
                    return;
                }

                SelectedTarget = refreshedTarget;

                /*
                 * Assign already appends newly assigned tickets to the route.
                 * If the dispatcher dropped in blank space, that is the desired result.
                 */
                if (!insertBeforeTicketId.HasValue ||
                    cleanTicketIds.Contains(insertBeforeTicketId.Value))
                {
                    StatusText =
                        $"Added {cleanTicketIds.Count} ticket(s) to {refreshedTarget.PrimaryText}. " +
                        "Save & Publish to send the updated list.";

                    return;
                }

                var currentOrder = refreshedTarget.AssignedTickets
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.AssignmentId)
                    .ToList();

                var insertedTicketIdSet = cleanTicketIds.ToHashSet();

                var insertedTickets = currentOrder
                    .Where(x => insertedTicketIdSet.Contains(x.TicketId))
                    .ToList();

                if (insertedTickets.Count == 0)
                {
                    StatusText =
                        $"Added ticket(s) to {refreshedTarget.PrimaryText}. " +
                        "Save & Publish to send the updated list.";

                    return;
                }

                var remainingTickets = currentOrder
                    .Where(x => !insertedTicketIdSet.Contains(x.TicketId))
                    .ToList();

                var insertionIndex = remainingTickets
                    .FindIndex(x => x.TicketId == insertBeforeTicketId.Value);

                if (insertionIndex < 0)
                    insertionIndex = remainingTickets.Count;

                remainingTickets.InsertRange(insertionIndex, insertedTickets);

                await SaveDraggedAssignmentOrderAsync(remainingTickets);

                StatusText =
                    $"Added {insertedTickets.Count} ticket(s) to {refreshedTarget.PrimaryText}. " +
                    "Save & Publish to send the updated route.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Assign failed: {ex.Body ?? ex.Message}";

                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Assign Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Assign failed: " + ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "Assign Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isAssigningByDragDrop = false;
                _draggedPoolTicket = null;
                HideBusyOverlay();
            }
        }

        private async Task SaveDraggedAssignmentOrderAsync(List<DailyAssignedTicketDto> orderedTickets)
        {
            var target = SelectedTarget;

            if (target == null || orderedTickets.Count == 0)
                return;

            ShowBusyOverlay("Updating ticket order...");

            try
            {
                _isReorderingByDragDrop = true;
                StatusText = "Updating ticket order...";

                var req = new ReorderDailyTicketAssignmentsRequest
                {
                    WorkDate = Board.WorkDate,
                    TargetType = target.TargetType,
                    TruckId = target.TruckId,
                    TechnicianId = target.TechnicianId,
                    TicketIdsInOrder = orderedTickets
                        .Select(x => x.TicketId)
                        .ToList(),
                    UpdatedBy = Environment.UserName
                };

                await _api.PostAsync<ReorderDailyTicketAssignmentsRequest, ReorderDailyTicketAssignmentsResponse>(
                    "api/daily-assignments/reorder",
                    req);

                await LoadBoardAsync();

                StatusText = "Ticket order updated. Save & Publish to send the new route order.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Reorder failed: {ex.Body ?? ex.Message}";

                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Reorder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Reorder failed: " + ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "Reorder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isReorderingByDragDrop = false;
                _draggedAssignedTicket = null;
                HideBusyOverlay();
            }
        }

        private async void MoveAssignedTicketUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DailyAssignedTicketDto ticket)
                return;

            await MoveAssignedTicketAsync(ticket, -1);
        }

        private async void MoveAssignedTicketDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DailyAssignedTicketDto ticket)
                return;

            await MoveAssignedTicketAsync(ticket, 1);
        }

        private async void ClearSelectedTargetList_Click(object sender, RoutedEventArgs e)
        {
            var target = SelectedTarget;

            if (target == null)
                return;

            var ticketIds = target.AssignedTickets
                .Select(x => x.TicketId)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return;

            var confirm = MessageBox.Show(
                $"Clear and publish an empty task list for {target.PrimaryText}?\n\n" +
                $"This will remove all {ticketIds.Count} ticket(s) from this route, clear the field tech My Tasks list for this target, " +
                "and return eligible tickets to unassigned.",
                "Clear Assignment List",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            ShowBusyOverlay($"Clearing and publishing empty list for {target.PrimaryText}...");

            try
            {
                StatusText = $"Clearing and publishing empty list for {target.PrimaryText}...";

                var removeReq = new RemoveDailyTicketAssignmentsRequest
                {
                    WorkDate = Board.WorkDate,
                    TicketIds = ticketIds,
                    UpdatedBy = Environment.UserName
                };

                await _api.PostAsync<RemoveDailyTicketAssignmentsRequest, RemoveDailyTicketAssignmentsResponse>(
                    "api/daily-assignments/remove",
                    removeReq);

                var publishReq = new PublishDailyAssignmentTargetRequest
                {
                    WorkDate = Board.WorkDate,
                    TargetType = target.TargetType,
                    TruckId = target.TruckId,
                    TechnicianId = target.TechnicianId,
                    PublishedBy = Environment.UserName
                };

                var publishResult = await _api.PostAsync<PublishDailyAssignmentTargetRequest, PublishDailyAssignmentTargetResponse>(
                    "api/daily-assignments/publish-target",
                    publishReq);

                await LoadBoardAsync();

                StatusText =
                    publishResult == null
                        ? $"Cleared and published empty list for {target.PrimaryText}."
                        : $"Cleared and published empty list for {target.PrimaryText} as version {publishResult.PublishedVersion}.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Clear list failed: {ex.Body ?? ex.Message}";

                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Clear List Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Clear list failed: " + ex.Message;

                MessageBox.Show(
                    ex.Message,
                    "Clear List Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private async Task MoveAssignedTicketAsync(DailyAssignedTicketDto ticket, int direction)
        {
            var target = SelectedTarget;

            if (target == null)
                return;

            var orderedTickets = target.AssignedTickets
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.AssignmentId)
                .ToList();

            var currentIndex = orderedTickets.FindIndex(x => x.TicketId == ticket.TicketId);

            if (currentIndex < 0)
                return;

            var newIndex = currentIndex + direction;

            if (newIndex < 0 || newIndex >= orderedTickets.Count)
                return;

            var moving = orderedTickets[currentIndex];
            orderedTickets.RemoveAt(currentIndex);
            orderedTickets.Insert(newIndex, moving);

            ShowBusyOverlay("Updating ticket order...");

            try
            {
                StatusText = "Updating ticket order...";

                var req = new ReorderDailyTicketAssignmentsRequest
                {
                    WorkDate = Board.WorkDate,
                    TargetType = target.TargetType,
                    TruckId = target.TruckId,
                    TechnicianId = target.TechnicianId,
                    TicketIdsInOrder = orderedTickets.Select(x => x.TicketId).ToList(),
                    UpdatedBy = Environment.UserName
                };

                await _api.PostAsync<ReorderDailyTicketAssignmentsRequest, ReorderDailyTicketAssignmentsResponse>(
                    "api/daily-assignments/reorder",
                    req);

                await LoadBoardAsync();

                StatusText = "Ticket order updated.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Reorder failed: {ex.Body ?? ex.Message}";
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Reorder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Reorder failed: " + ex.Message;
                MessageBox.Show(
                    ex.Message,
                    "Reorder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private async void PublishSelectedTarget_Click(object sender, RoutedEventArgs e)
        {
            var target = SelectedTarget;

            if (target == null)
            {
                MessageBox.Show(
                    "Select a crew or technician first.",
                    "Save & Publish",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmText = target.AssignedTicketCount == 0
                ? $"Save and publish an EMPTY task list for {target.PrimaryText}?\n\n" +
                  "This will remove all tickets from the field tech task list for this crew/technician."
                : $"Save and publish the current list for {target.PrimaryText}?\n\n" +
                  $"Ticket count: {target.AssignedTicketCount}\n\n" +
                  "Only this selected crew/technician will receive the updated list.";

            var confirm = MessageBox.Show(
                confirmText,
                "Save & Publish",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            ShowBusyOverlay($"Saving and publishing list for {target.PrimaryText}...");

            try
            {
                StatusText = $"Saving and publishing list for {target.PrimaryText}...";

                var req = new PublishDailyAssignmentTargetRequest
                {
                    WorkDate = Board.WorkDate,
                    TargetType = target.TargetType,
                    TruckId = target.TruckId,
                    TechnicianId = target.TechnicianId,
                    PublishedBy = Environment.UserName
                };

                var result = await _api.PostAsync<PublishDailyAssignmentTargetRequest, PublishDailyAssignmentTargetResponse>(
                    "api/daily-assignments/publish-target",
                    req);

                await LoadBoardAsync();

                if (target.AssignedTicketCount == 0)
                {
                    StatusText = $"Saved and published an empty list for {target.PrimaryText}.";
                }
                else
                {
                    StatusText =
                        result == null
                            ? $"Saved and published list for {target.PrimaryText}."
                            : $"Saved and published {result.PublishedCount} ticket(s) for {target.PrimaryText} as version {result.PublishedVersion}.";
                }
            }
            catch (ApiClient.ApiException ex)
            {
                StatusText = $"Save & Publish failed: {ex.Body ?? ex.Message}";
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Save & Publish Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText = "Save & Publish failed: " + ex.Message;
                MessageBox.Show(
                    ex.Message,
                    "Save & Publish Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private void RebuildAssignmentTargets(string? preferredTargetKey)
        {
            _assignmentTargets.Clear();

            var targets = new List<AssignmentTargetVm>();

            targets.AddRange(Board.TruckTargets
                .Where(x => x.Technicians.Count > 0)
                .Select(AssignmentTargetVm.FromDto));

            targets.AddRange(Board.TechnicianTargets
                .Select(AssignmentTargetVm.FromDto));

            foreach (var target in targets
             .OrderBy(
                 x => x.SortText,
                 StringComparer.OrdinalIgnoreCase)
             .ThenBy(
                 x => x.SecondaryText,
                 StringComparer.OrdinalIgnoreCase)
             .ThenBy(
                 x => x.TargetKey,
                 StringComparer.OrdinalIgnoreCase))
            {
                _assignmentTargets.Add(target);
            }

            SelectedTarget =
                _assignmentTargets.FirstOrDefault(x => x.TargetKey == preferredTargetKey)
                ?? _assignmentTargets.FirstOrDefault();
        }

        private void RefreshAllBindings()
        {
            OnPropertyChanged(nameof(Board));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(TicketPoolCount));
            OnPropertyChanged(nameof(FilteredTicketPool));
            OnPropertyChanged(nameof(VisibleTicketPoolCount));
            OnPropertyChanged(nameof(SelectedTicketCount));
            OnPropertyChanged(nameof(HasSelectedTickets));
            OnPropertyChanged(nameof(TicketPoolSummaryText));
            OnPropertyChanged(nameof(AssignmentTargets));
            OnPropertyChanged(nameof(AssignedTicketCount));
            OnPropertyChanged(nameof(FieldCompleteCount));
            OnPropertyChanged(nameof(BoardStatusText));
            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(HasSelectedTarget));
            OnPropertyChanged(nameof(SelectedTargetSubtitle));
            OnPropertyChanged(nameof(SelectedTargetPublishStatusText));
            OnPropertyChanged(nameof(HasSelectedTargetAssignedTickets));
        }

        private static void NormalizeBoardForDisplay(DailyAssignmentsBoardDto board)
        {
            board.TruckTargets = board.TruckTargets
                .OrderBy(x => x.Technicians.Count == 0 ? 1 : 0)
                .ThenBy(GetTargetNameSortKey)
                .ThenBy(x => x.TruckNumber)
                .ToList();

            board.TechnicianTargets = board.TechnicianTargets
                .OrderBy(x => x.TechnicianName)
                .ToList();
        }

        private static string GetTargetNameSortKey(DailyAssignmentTargetDto target)
        {
            if (target.Technicians.Count > 0)
            {
                return string.Join(" / ",
                    target.Technicians
                        .Select(x => x.Name)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x));
            }

            return target.TechnicianName
                   ?? target.TruckNumber
                   ?? "";
        }

        private async Task<AssignDailyTicketsResponse?> AssignTicketsWithConflictWarningAsync(
            AssignDailyTicketsRequest request, string targetName)
        {
            try
            {
                return await _api.PostAsync<AssignDailyTicketsRequest, AssignDailyTicketsResponse>(
                    "api/daily-assignments/assign",
                    request);
            }
            catch (ApiClient.ApiException ex) when (IsConflictResponse(ex))
            {
                var warningText = CleanApiMessage(ex.Body ?? ex.Message);

                var confirm = MessageBox.Show(
                    warningText,
                    "Assignment Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    StatusText = $"Assignment to {targetName} canceled.";
                    return null;
                }

                request.ConfirmConflictWarnings = true;

                return await _api.PostAsync<AssignDailyTicketsRequest, AssignDailyTicketsResponse>(
                    "api/daily-assignments/assign",
                    request);
            }
        }

        private static bool IsConflictResponse(ApiClient.ApiException ex)
        {
            var status = ex.StatusCode.ToString();

            return status.Equals("Conflict", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("409", StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanApiMessage(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Length >= 2 &&
                text.StartsWith("\"") &&
                text.EndsWith("\""))
            {
                text = text[1..^1]
                    .Replace("\\r\\n", Environment.NewLine)
                    .Replace("\\n", Environment.NewLine)
                    .Replace("\\\"", "\"");
            }

            return string.IsNullOrWhiteSpace(text)
                ? "Assignment warning."
                : text;
        }

        private static T? FindVisualParent<T>(DependencyObject? source)
            where T : DependencyObject
        {
            var current = source;

            while (current != null)
            {
                if (current is T match)
                    return match;

                current = GetParentSafely(current);
            }

            return null;
        }

        private static DependencyObject? GetParentSafely(DependencyObject current)
        {
            if (current is Visual ||
                current is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }

            if (current is FrameworkContentElement contentElement)
            {
                return contentElement.Parent;
            }

            return LogicalTreeHelper.GetParent(current);
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativeCursorPoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeCursorPoint
        {
            public int X;
            public int Y;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class TicketDragGhostVm
    {
        public DailyAssignmentTicketDto Ticket { get; init; } = new();

        public int AdditionalCount { get; init; }

        public string AdditionalCountText =>
            AdditionalCount <= 0
                ? ""
                : $"+{AdditionalCount} more";
    }

    public sealed class AssignmentTargetVm
    {
        public string TargetKey { get; set; } = "";
        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        public string PrimaryText { get; set; } = "";

        public string PrimaryLine1 { get; set; } = "";
        public string PrimaryLine2 { get; set; } = "";

        public string SecondaryText { get; set; } = "";
        public string SortText { get; set; } = "";

        public ObservableCollection<DailyAssignedTicketDto> AssignedTickets { get; set; } = new();

        public int AssignedTicketCount => AssignedTickets.Count;

        public bool HasNoTickets => AssignedTicketCount == 0;

        public bool HasUnpublishedChanges => AssignedTickets.Any(x => !x.IsPublished);

        public string ComboStatusText
        {
            get
            {
                if (AssignedTicketCount == 0)
                    return "No Tickets Assigned";

                return HasUnpublishedChanges
                    ? $"Unpublished Changes ({AssignedTicketCount} tickets)"
                    : $"Published ({AssignedTicketCount} tickets)";
            }
        }

        public string PublishStatusText
        {
            get
            {
                if (AssignedTicketCount == 0)
                    return "No tickets assigned";

                return HasUnpublishedChanges
                    ? "Unpublished changes"
                    : "Published";
            }
        }

        public static AssignmentTargetVm FromDto(DailyAssignmentTargetDto dto)
        {
            var isTruck = dto.TruckId.HasValue && dto.Technicians.Count > 0;

            var names = dto.Technicians
                .Select(x => FormatLastFirst(
                    x.FirstName,
                    x.LastName,
                    x.Name))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var individualName = FormatLastFirst(dto.TechnicianName);

            var primary = isTruck
                ? names.Count == 0
                    ? "Unassigned Truck"
                    : FormatCrewNames(names)
                : string.IsNullOrWhiteSpace(individualName)
                    ? "Unknown Technician"
                    : individualName;

            var secondary = isTruck
                ? $"Truck {dto.TruckNumber} · {dto.TruckStyleName}"
                : dto.TechnicianTitle ?? "";

            var sortText = isTruck
                ? names.FirstOrDefault() ?? primary
                : primary;

            var primaryLine1 = isTruck
                ? names.FirstOrDefault() ?? primary
                : primary;

            var primaryLine2 = isTruck && names.Count > 1
                ? names.Count == 2
                    ? names[1]
                    : $"{names[1]} +{names.Count - 2} more"
                : "";

            return new AssignmentTargetVm
            {
                TargetKey = dto.TargetKey,
                TargetType = dto.TargetType,
                TruckId = dto.TruckId,
                TechnicianId = dto.TechnicianId,

                PrimaryText = primary,
                PrimaryLine1 = primaryLine1,
                PrimaryLine2 = primaryLine2,

                SecondaryText = secondary,
                SortText = sortText,

                AssignedTickets = BuildOrderedAssignedTickets(dto.AssignedTickets)
            };
        }

        // Produces a stable one-based route for display. The API remains responsible
        // for persistence; this only prevents stale or gapped sort values from
        // appearing as route numbers in the current UI.
        private static ObservableCollection<DailyAssignedTicketDto>
            BuildOrderedAssignedTickets(IEnumerable<DailyAssignedTicketDto> tickets)
        {
            var orderedTickets = tickets
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.AssignmentId)
                .ToList();

            for (var index = 0;
                 index < orderedTickets.Count;
                 index++)
            {
                orderedTickets[index].SortOrder =
                    index + 1;
            }

            return new ObservableCollection<DailyAssignedTicketDto>(
                orderedTickets);
        }

        private static string FormatLastFirst(
            string? firstName,
            string? lastName,
            string? fallbackName)
        {
            var first = (firstName ?? "").Trim();
            var last = (lastName ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(last) &&
                !string.IsNullOrWhiteSpace(first))
            {
                return $"{last}, {first}";
            }

            if (!string.IsNullOrWhiteSpace(last))
                return last;

            if (!string.IsNullOrWhiteSpace(first))
                return first;

            return FormatLastFirst(fallbackName);
        }

        private static string FormatLastFirst(string? fullName)
        {
            var name = (fullName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return "";

            // Already Last, First.
            if (name.Contains(','))
                return name;

            var parts = name.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
                return parts[0];

            var lastName = parts[^1];
            var firstName = string.Join(" ", parts[..^1]);

            return $"{lastName}, {firstName}";
        }

        private static string FormatCrewNames(IReadOnlyList<string> names)
        {
            if (names.Count == 0)
                return "Unknown Crew";

            if (names.Count == 1)
                return names[0];

            if (names.Count == 2)
                return $"{names[0]} & {names[1]}";

            return string.Join(", ", names.Take(names.Count - 1)) +
                   " & " +
                   names.Last();
        }
    }
}