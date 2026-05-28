#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Dispatcher.DailyAssignments;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Data;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class DailyAssignmentsPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ApiClient _api = new("https://localhost:7140");
        private readonly DispatcherTimer _ticketSearchTimer;
        private readonly ObservableCollection<DailyAssignmentTicketDto> _filteredTicketPool = new();
        private readonly ObservableCollection<AssignmentTargetVm> _assignmentTargets = new();
        private readonly HashSet<long> _selectedTicketIds = new();

        private bool _hasLoaded;
        private bool _busyLoading;
        private bool _syncingTicketSelection;
        private bool _includeAssignedTickets;

        private string _statusText = "Ready.";
        private string _ticketSearchText = "";

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

        private async Task LoadBoardAsync()
        {
            if (_busyLoading)
                return;

            try
            {
                _busyLoading = true;
                StatusText = "Loading daily assignments...";

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
            var q = (_ticketSearchText ?? string.Empty).Trim();

            IEnumerable<DailyAssignmentTicketDto> filtered = Board.TicketPool;

            if (!_includeAssignedTickets)
                filtered = filtered.Where(t => t.CurrentAssignmentId == null);

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

                var result = await _api.PostAsync<AssignDailyTicketsRequest, AssignDailyTicketsResponse>(
                    "api/daily-assignments/assign",
                    req);

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
                $"Clear all {ticketIds.Count} ticket(s) from {target.PrimaryText}?\n\n" +
                "This clears the dispatcher list only. Click Save & Publish after this if the field tech task list should also be emptied.",
                "Clear Assignment List",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            await RemoveAssignedTicketsAsync(
                ticketIds,
                $"Clearing list for {target.PrimaryText}...",
                $"Cleared {ticketIds.Count} ticket(s) from {target.PrimaryText}. Click Save & Publish to update field tech tasks.");
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
                         .OrderBy(x => x.HasNoTickets ? 0 : 1)
                         .ThenBy(x => x.PrimaryText)
                         .ThenBy(x => x.SecondaryText))
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

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class OneBasedIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
                return (index + 1).ToString(culture);

            if (int.TryParse(value?.ToString(), out var parsed))
                return (parsed + 1).ToString(culture);

            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class AssignmentTargetVm
    {
        public string TargetKey { get; set; } = "";
        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        public string PrimaryText { get; set; } = "";
        public string SecondaryText { get; set; } = "";

        public List<DailyAssignedTicketDto> AssignedTickets { get; set; } = new();

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
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var primary = isTruck
                ? names.Count == 0 ? "Unassigned Truck" : FormatCrewNames(names)
                : string.IsNullOrWhiteSpace(dto.TechnicianName) ? "Unknown Technician" : dto.TechnicianName!;

            var secondary = isTruck
                ? $"Truck {dto.TruckNumber} · {dto.TruckStyleName}"
                : dto.TechnicianTitle ?? "";

            return new AssignmentTargetVm
            {
                TargetKey = dto.TargetKey,
                TargetType = dto.TargetType,
                TruckId = dto.TruckId,
                TechnicianId = dto.TechnicianId,
                PrimaryText = primary,
                SecondaryText = secondary,
                AssignedTickets = dto.AssignedTickets
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.AssignmentId)
                    .ToList()
            };
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