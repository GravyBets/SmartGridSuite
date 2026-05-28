using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using SmartGridSuite.Contracts.Tickets;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using SmartGridSuite.Contracts.SiteNotes;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using System.Security.Principal;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TicketsPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public sealed class StatusFilterOption : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public string Name { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public StatusFilterOption(string name, bool isSelected)
            {
                Name = name;
                _isSelected = isSelected;
            }
        }

        private readonly ObservableCollection<DispatchTicket> _tickets = new();
        private readonly TicketsApi _ticketsApi = new TicketsApi(new ApiClient("https://localhost:7140/"));
        private readonly TechniciansApi _techniciansApi;        
        private readonly HashSet<string> _knownTechs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _createdByDisplayByUserId = new(StringComparer.OrdinalIgnoreCase);
        private string _selectedTicketOriginalDispatchNotes = "";
        private bool _isSummaryExpanded;
        private bool _isSummaryLoading;

        private readonly SiteNotesApi _siteNotesApi = new(new ApiClient("https://localhost:7140/"));

        private readonly ObservableCollection<SiteNoteDto> _selectedTicketSiteNotes = new();
        public int SelectedTicketSiteNotesCount => _selectedTicketSiteNotes.Count;

        private string _selectedTicketOriginalTechNotes = "";

        private enum TicketQuickFilter
        {
            None,
            MissingProblems,
            Unassigned,
            ReadyToAssign,
            Assigned
        }

        private TicketQuickFilter _activeQuickFilter = TicketQuickFilter.None;

        private bool _detailsOpen;

        private readonly DispatcherTimer _searchDebounceTimer;
        private CancellationTokenSource? _ticketQueryCts;

        public ICollectionView TicketsView { get; }

        public ObservableCollection<StatusFilterOption> StatusOptions { get; } = new();

        
        private DispatchTicket? _selectedTicket;
        public DispatchTicket? SelectedTicket
        {
            get => _selectedTicket;
            set
            {
                if (ReferenceEquals(_selectedTicket, value)) return;

                _selectedTicket = value;

                _selectedTicketOriginalTechNotes = value?.Notes ?? "";
                _selectedTicketOriginalDispatchNotes = value?.DispatchNotes ?? "";

                OnPropertyChanged(nameof(SelectedTicket));
                OnPropertyChanged(nameof(SelectedTicketCreatedByDisplay));
                OnPropertyChanged(nameof(IsSelectedTicketClosed));
                OnPropertyChanged(nameof(CanEditSelectedTicket));
                OnPropertyChanged(nameof(SelectedTicketClosedLockText));

                _ = LoadSiteNotesForSelectedTicketAsync();

                Dispatcher.BeginInvoke(new Action(CollapseTicketDetailExpanders),
                    DispatcherPriority.Background);

                UpdateSaveDetailsButtonState();
            }
        }
        public string SelectedTicketCreatedByDisplay
                    => ResolveCreatedByDisplay(SelectedTicket?.CreatedBy);

        public ObservableCollection<SiteNoteDto> SelectedTicketSiteNotes => _selectedTicketSiteNotes;

        public bool IsSelectedTicketClosed
        {
            get
            {
                var status = SelectedTicket?.Status ?? "";

                return status.Equals("Closed", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Canceled", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool CanEditSelectedTicket => SelectedTicket != null && !IsSelectedTicketClosed;

        public string SelectedTicketClosedLockText =>
            IsSelectedTicketClosed
                ? "This ticket is closed. Reopen it before editing notes."
                : "";

        private bool _suppressFilterEvents;
        private bool _hasLoadedOnce;
        private bool _isInitialLoadRunning;

        private int _visibleTicketCount;
        public int VisibleTicketCount
        {
            get => _visibleTicketCount;
            set
            {
                if (_visibleTicketCount == value) return;
                _visibleTicketCount = value;
                OnPropertyChanged(nameof(VisibleTicketCount));
            }
        }

        private int _totalLoadedTicketCount;
        public int TotalLoadedTicketCount
        {
            get => _totalLoadedTicketCount;
            set
            {
                if (_totalLoadedTicketCount == value) return;
                _totalLoadedTicketCount = value;
                OnPropertyChanged(nameof(TotalLoadedTicketCount));
            }
        }

        private int _needsReviewCount;
        public int NeedsReviewCount
        {
            get => _needsReviewCount;
            set
            {
                if (_needsReviewCount == value) return;
                _needsReviewCount = value;
                OnPropertyChanged(nameof(NeedsReviewCount));
            }
        }

        private int _openCount;
        public int OpenCount
        {
            get => _openCount;
            set
            {
                if (_openCount == value) return;
                _openCount = value;
                OnPropertyChanged(nameof(OpenCount));
            }
        }

        private int _assignedCount;
        public int AssignedCount
        {
            get => _assignedCount;
            set
            {
                if (_assignedCount == value) return;
                _assignedCount = value;
                OnPropertyChanged(nameof(AssignedCount));
            }
        }

        private int _inProgressCount;
        public int InProgressCount
        {
            get => _inProgressCount;
            set
            {
                if (_inProgressCount == value) return;
                _inProgressCount = value;
                OnPropertyChanged(nameof(InProgressCount));
            }
        }

        private int _waitingDispatchCount;
        public int WaitingDispatchCount
        {
            get => _waitingDispatchCount;
            set
            {
                if (_waitingDispatchCount == value) return;
                _waitingDispatchCount = value;
                OnPropertyChanged(nameof(WaitingDispatchCount));
            }
        }

        private int _closedCount;
        public int ClosedCount
        {
            get => _closedCount;
            set
            {
                if (_closedCount == value) return;
                _closedCount = value;
                OnPropertyChanged(nameof(ClosedCount));
            }
        }

        public string SelectedStatusesSummary
        {
            get
            {
                var selected = StatusOptions.Where(x => x.IsSelected).Select(x => x.Name).ToList();

                if (selected.Count == 0)
                    return "No statuses";

                if (selected.Count == StatusOptions.Count)
                    return "All statuses";

                bool allOpen =
                    selected.Count == StatusOptions.Count - 1 &&
                    !selected.Contains("Closed", StringComparer.OrdinalIgnoreCase);

                if (allOpen)
                    return "All open";

                if (selected.Count <= 2)
                    return string.Join(", ", selected);

                return $"{selected.Count} selected";
            }
        }

        public TicketsPaneView()
        {
            InitializeComponent();
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            DataContext = this;


            TicketsView = CollectionViewSource.GetDefaultView(_tickets);
            TicketsView.SortDescriptions.Clear();
            TicketsView.SortDescriptions.Add(
                new SortDescription(nameof(DispatchTicket.LastActivityAt), ListSortDirection.Descending));
            _techniciansApi = new TechniciansApi(new ApiClient("https://localhost:7140"));

            TicketsGrid.ItemsSource = TicketsView;

            DateRangeFilter.ItemsSource = new[]
            {
                "All",
                "Last 24 Hours",
                "Last 7 Days",
                "Last 30 Days",
                "Last 3 Months",
                "Custom"
            };
            DateRangeFilter.SelectedIndex = 0;

            TechFilter.ItemsSource = new[] { "All", "(Unassigned)" };
            TechFilter.SelectedIndex = 0;

            InitializeStatusOptions();
            UpdateCustomDateVisibility();

            Loaded += TicketsPaneView_Loaded;
        }

        private async void AddSelectedTicketSiteNote_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditSelectedTicket || SelectedTicket == null)
                return;

            var site = SelectedTicket.Site?.Trim();

            if (string.IsNullOrWhiteSpace(site))
                return;

            await LoadKnownTechsFromApiAsync();

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

                await LoadSiteNotesForSelectedTicketAsync();
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

        private async void EditSelectedTicketSiteNote_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditSelectedTicket)
                return;

            if (sender is not Button button || button.Tag is not SiteNoteDto note)
                return;

            await LoadKnownTechsFromApiAsync();

            var site = SelectedTicket?.Site?.Trim() ?? "";

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

                await LoadSiteNotesForSelectedTicketAsync();
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

        private async void DeleteSelectedTicketSiteNote_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditSelectedTicket)
                return;

            if (sender is not Button button || button.Tag is not SiteNoteDto note)
                return;

            await LoadKnownTechsFromApiAsync();

            var confirm = MessageBox.Show(
                "Delete this site note?",
                "Delete Site Note",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await _siteNotesApi.DeleteAsync(note.Id, GetCurrentUserDisplayName());
                await LoadSiteNotesForSelectedTicketAsync();
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

        private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            await LoadTicketsFromApiAsync();
        }

        private void InitializeStatusOptions()
        {
            StatusOptions.Clear();

            // Closed is OFF by default.
            StatusOptions.Add(new StatusFilterOption("Needs Review", true));
            StatusOptions.Add(new StatusFilterOption("Open", true));
            StatusOptions.Add(new StatusFilterOption("Assigned", true));
            StatusOptions.Add(new StatusFilterOption("In Progress", true));
            StatusOptions.Add(new StatusFilterOption("Waiting Dispatch", true));
            StatusOptions.Add(new StatusFilterOption("Closed", false));

            OnPropertyChanged(nameof(SelectedStatusesSummary));
        }

        private async void TicketsPaneView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedOnce || _isInitialLoadRunning)
                return;

            _isInitialLoadRunning = true;

            try
            {
                await LoadKnownTechsFromApiAsync();
                RebuildTechFilterFromKnownTechs();
                await LoadTicketsFromApiAsync();
                _hasLoadedOnce = true;
            }
            finally
            {
                _isInitialLoadRunning = false;
            }
        }
        
        private void RebuildTechFilterFromKnownTechs()
        {
            var previous = TechFilter.SelectedItem as string ?? "All";

            var items = new List<string> { "All", "(Unassigned)" };
            items.AddRange(_knownTechs.OrderBy(x => x));

            if (!string.IsNullOrWhiteSpace(previous) && !items.Contains(previous))
                items.Insert(2, previous);

            _suppressFilterEvents = true;
            try
            {
                TechFilter.ItemsSource = items;
                TechFilter.SelectedItem = items.Contains(previous) ? previous : "All";
            }
            finally
            {
                _suppressFilterEvents = false;
            }
        }

        private async Task LoadKnownTechsFromApiAsync(CancellationToken ct = default)
        {
            try
            {
                var techs = await _techniciansApi.GetTechniciansAsync(ct: ct);

                _knownTechs.Clear();
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
                        _knownTechs.Add(displayName);

                        AddCreatedByDisplayAlias(displayName, displayName);
                        AddCreatedByDisplayAlias(userId, displayName);
                        AddCreatedByDisplayAlias(employeeId, displayName);
                    }
                }

                RebuildTechFilterFromKnownTechs();
                OnPropertyChanged(nameof(SelectedTicketCreatedByDisplay));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load technicians.\n\n{ex.Message}",
                    "API Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task LoadTicketsFromApiAsync(CancellationToken ct = default)
        {
            _ticketQueryCts?.Cancel();
            _ticketQueryCts?.Dispose();

            _ticketQueryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var queryCt = _ticketQueryCts.Token;

            try
            {
                var req = BuildTicketQueryRequest();

                var response = await _ticketsApi.QueryTicketsAsync(req, queryCt);

                _tickets.Clear();

                foreach (var dto in response.Items)
                    _tickets.Add(Map(dto));

                VisibleTicketCount = response.TotalCount;
                TotalLoadedTicketCount = response.TotalCount;

                TicketsView.Refresh();
                UpdateTicketListUiState();
            }
            catch (OperationCanceledException)
            {
                // Expected when filters/search change quickly.
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load tickets from API.\n\n{ex.Message}",
                    "API Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private TicketQueryRequest BuildTicketQueryRequest()
        {
            var (from, to) = GetLastActivityDateRangeFromUi();

            return new TicketQueryRequest
            {
                Search = SearchBox?.Text?.Trim(),
                Statuses = GetSelectedStatuses()
                    .OrderBy(x => x)
                    .ToList(),

                AssignedTech = TechFilter?.SelectedItem as string ?? "All",

                DateField = "LastActivity",
                From = from,
                To = to,

                QuickFilter = GetActiveQuickFilterApiValue(),

                Skip = 0,
                Take = 2000
            };
        }

        private string? GetActiveQuickFilterApiValue()
        {
            return _activeQuickFilter switch
            {
                TicketQuickFilter.MissingProblems => "MissingProblems",
                TicketQuickFilter.Unassigned => "Unassigned",
                TicketQuickFilter.ReadyToAssign => "ReadyToAssign",
                TicketQuickFilter.Assigned => "Assigned",
                _ => null
            };
        }

        private async Task LoadSummaryFromApiAsync(CancellationToken ct = default)
        {
            var dtos = await _ticketsApi.GetTicketsAsync(
                status: null,
                tech: null,
                from: null,
                to: null,
                ct);

            TotalLoadedTicketCount = dtos.Count;
            NeedsReviewCount = dtos.Count(x => string.Equals(x.Status, "Needs Review", StringComparison.OrdinalIgnoreCase));
            OpenCount = dtos.Count(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase));
            AssignedCount = dtos.Count(x => string.Equals(x.Status, "Assigned", StringComparison.OrdinalIgnoreCase));
            InProgressCount = dtos.Count(x => string.Equals(x.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            WaitingDispatchCount = dtos.Count(x => string.Equals(x.Status, "Waiting Dispatch", StringComparison.OrdinalIgnoreCase));
            ClosedCount = dtos.Count(x => string.Equals(x.Status, "Closed", StringComparison.OrdinalIgnoreCase));

            UpdateVisibleTicketCount();
        }

        private static DispatchTicket Map(TicketListItemDto dto)
        {
            var rawWorkOrderType = (dto.WorkOrderClass ?? "").Trim();
            var woClass = ParseWorkOrderClass(rawWorkOrderType);

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
                WoClass = woClass,
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

        private HashSet<string> GetSelectedStatuses()
        {
            return StatusOptions
                .Where(x => x.IsSelected)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private (DateTime? from, DateTime? to) GetLastActivityDateRangeFromUi()
        {
            var dateRange = DateRangeFilter?.SelectedItem as string ?? "All";

            return dateRange switch
            {
                "Last 24 Hours" => (DateTime.Now.AddHours(-24), null),
                "Last 7 Days" => (DateTime.Now.AddDays(-7), null),
                "Last 30 Days" => (DateTime.Now.AddDays(-30), null),
                "Last 3 Months" => (DateTime.Now.AddMonths(-3), null),
                "Custom" => (FromDatePicker.SelectedDate?.Date, ToDatePicker.SelectedDate?.Date),
                _ => (null, null)
            };
        }

        private bool FilterTicket(object obj)
        {
            if (obj is not DispatchTicket t)
                return false;

            // Statuses
            var selectedStatuses = GetSelectedStatuses();
            if (selectedStatuses.Count == 0)
                return false;

            if (!selectedStatuses.Contains(t.Status ?? ""))
                return false;

            if (!PassesQuickFilter(t))
                return false;

            // Tech
            var tech = TechFilter?.SelectedItem as string ?? "All";
            if (tech == "(Unassigned)" && t.AssignedTech != "(Unassigned)")
                return false;

            if (tech != "All" &&
                tech != "(Unassigned)" &&
                !string.Equals(t.AssignedTech, tech, StringComparison.OrdinalIgnoreCase))
                return false;

            // Last activity date range
            var (from, to) = GetLastActivityDateRangeFromUi();
            var activityDate = t.LastActivityAt;

            if (DateRangeFilter?.SelectedItem as string == "Custom")
            {
                if (from.HasValue && activityDate < from.Value.Date)
                    return false;

                if (to.HasValue && activityDate >= to.Value.Date.AddDays(1))
                    return false;
            }
            else
            {
                if (from.HasValue && activityDate < from.Value)
                    return false;
            }

            // Search
            var q = (SearchBox?.Text ?? "").Trim();
            if (q.Length == 0)
                return true;

            bool Match(string? s) =>
                !string.IsNullOrWhiteSpace(s) &&
                s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            return Match(t.Site) ||
                   Match(t.NotificationName) ||
                   Match(t.Notification) ||
                   Match(t.CurrentWorkOrder) ||
                   Match(t.WorkOrderClassLabel) ||
                   Match(t.GroupCode) ||
                   Match(t.Status) ||
                   Match(t.AssignedTech) ||
                   Match(t.Problem) ||
                   Match(t.Summary) ||
                   Match(t.Notes) ||
                   Match(t.DispatchNotes) ||
                   Match(t.CreatedBy);
        }

        private bool PassesQuickFilter(DispatchTicket ticket)
        {
            return _activeQuickFilter switch
            {
                TicketQuickFilter.MissingProblems =>
                    string.IsNullOrWhiteSpace(ticket.Problem),

                TicketQuickFilter.Unassigned =>
                    IsUnassigned(ticket),

                TicketQuickFilter.ReadyToAssign =>
                    IsReadyToAssign(ticket),

                TicketQuickFilter.Assigned =>
                    string.Equals(ticket.Status, "Assigned", StringComparison.OrdinalIgnoreCase),

                _ => true
            };
        }

        private static bool IsUnassigned(DispatchTicket ticket)
        {
            return string.IsNullOrWhiteSpace(ticket.AssignedTech)
                || string.Equals(ticket.AssignedTech, "(Unassigned)", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReadyToAssign(DispatchTicket ticket)
        {
            return string.Equals(ticket.Status, "Open", StringComparison.OrdinalIgnoreCase)
                && IsUnassigned(ticket)
                && !string.IsNullOrWhiteSpace(ticket.Site);
        }

        private void RefreshView()
        {
            TicketsView?.Refresh();
            UpdateVisibleTicketCount();
        }

        private void UpdateVisibleTicketCount()
        {
            VisibleTicketCount = TicketsView.Cast<DispatchTicket>().Count();
            UpdateTicketListUiState();
        }

        private void UpdateTicketListUiState()
        {
            var selectedCount = TicketsGrid?.SelectedItems
                .OfType<DispatchTicket>()
                .Count() ?? 0;

            if (BulkSetProblemButton != null)
                BulkSetProblemButton.IsEnabled = selectedCount > 0;

            if (AssignSelectedButton != null)
                AssignSelectedButton.IsEnabled = selectedCount > 0;

            if (ClearSelectionButton != null)
                ClearSelectionButton.IsEnabled = selectedCount > 0;

            if (SelectVisibleTicketsButton != null)
                SelectVisibleTicketsButton.IsEnabled = VisibleTicketCount > 0;

            if (SelectedCountTextBlock != null)
            {
                SelectedCountTextBlock.Text = selectedCount == 0
                    ? ""
                    : $"{selectedCount} selected";
            }

            if (MissingProblemsButton != null)
            {
                MissingProblemsButton.Content = IsQuickFilterActive(TicketQuickFilter.MissingProblems)
                    ? "Showing Missing"
                    : "Missing Problems";
            }

            if (UnassignedTicketsButton != null)
            {
                UnassignedTicketsButton.Content = IsQuickFilterActive(TicketQuickFilter.Unassigned)
                    ? "Showing Unassigned"
                    : "Unassigned";
            }

            if (ReadyToAssignButton != null)
            {
                ReadyToAssignButton.Content = IsQuickFilterActive(TicketQuickFilter.ReadyToAssign)
                    ? "Showing Ready"
                    : "Ready to Assign";
            }

            if (AssignedTicketsButton != null)
            {
                AssignedTicketsButton.Content = IsQuickFilterActive(TicketQuickFilter.Assigned)
                    ? "Showing Assigned"
                    : "Assigned";
            }

            var hasQuickFilter = _activeQuickFilter != TicketQuickFilter.None;
            var quickFilterName = GetActiveQuickFilterDisplayName();

            if (ClearMissingProblemsButton != null)
            {
                ClearMissingProblemsButton.Visibility = hasQuickFilter
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (MissingProblemsBadge != null)
            {
                MissingProblemsBadge.Visibility = hasQuickFilter
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (MissingProblemsBadgeText != null)
            {
                MissingProblemsBadgeText.Text = hasQuickFilter
                    ? $"{VisibleTicketCount} {quickFilterName.ToLower()} ticket(s)"
                    : "";
            }
        }

        private string GetActiveQuickFilterDisplayName()
        {
            return _activeQuickFilter switch
            {
                TicketQuickFilter.MissingProblems => "Missing Problems",
                TicketQuickFilter.Unassigned => "Unassigned",
                TicketQuickFilter.ReadyToAssign => "Ready to Assign",
                TicketQuickFilter.Assigned => "Assigned",
                _ => ""
            };
        }

        private bool IsQuickFilterActive(TicketQuickFilter filter)
        {
            return _activeQuickFilter == filter;
        }

        private async void SetSelectedStatuses(params string[] statusNames)
        {
            SetSelectedStatusesWithoutRefresh(statusNames);
            await LoadTicketsFromApiAsync();
        }

        private void SetSelectedStatusesWithoutRefresh(params string[] statusNames)
        {
            var selected = new HashSet<string>(statusNames, StringComparer.OrdinalIgnoreCase);

            foreach (var option in StatusOptions)
                option.IsSelected = selected.Contains(option.Name);

            OnPropertyChanged(nameof(SelectedStatusesSummary));
        }

        private void UpdateCustomDateVisibility()
        {
            var sel = DateRangeFilter?.SelectedItem as string ?? "All";
            bool isCustom = sel == "Custom";

            var spacerWidth = isCustom ? new GridLength(12) : new GridLength(0);
            var dateWidth = isCustom ? new GridLength(170) : new GridLength(0);

            CustomFromSpacerCol.Width = spacerWidth;
            CustomFromCol.Width = dateWidth;
            CustomToSpacerCol.Width = spacerWidth;
            CustomToCol.Width = dateWidth;

            FromDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ToDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            if (isCustom && FromDatePicker.SelectedDate == null && ToDatePicker.SelectedDate == null)
            {
                ToDatePicker.SelectedDate = DateTime.Today;
                FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
            }
        }

        private void UpdateDetailsVisibility()
        {
            if (!_detailsOpen || SelectedTicket == null)
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
                DetailsSplitter.Visibility = Visibility.Collapsed;
                DetailsSplitterCol.Width = new GridLength(0);
                DetailsCol.Width = new GridLength(0);
                return;
            }

            DetailsSplitterCol.Width = new GridLength(10);
            DetailsCol.Width = new GridLength(500);
            DetailsSplitter.Visibility = Visibility.Visible;
            DetailsPanel.Visibility = Visibility.Visible;
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTicketsFromApiAsync();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents)
                return;

            UpdateCustomDateVisibility();
            await LoadTicketsFromApiAsync();
        }

        private async void InlineCustomDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents)
                return;

            if ((DateRangeFilter?.SelectedItem as string) != "Custom")
                return;

            await LoadTicketsFromApiAsync();
        }

        private void StatusFilterButton_Click(object sender, RoutedEventArgs e)
        {
            StatusPopup.IsOpen = !StatusPopup.IsOpen;
        }

        private async void StatusOption_Changed(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged(nameof(SelectedStatusesSummary));
            await LoadTicketsFromApiAsync();
        }

        private void SelectAllOpenStatuses_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedStatuses(
                "Needs Review",
                "Open",
                "Assigned",
                "In Progress",
                "Waiting Dispatch");
        }

        private void SelectAllStatuses_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedStatuses(
                "Needs Review",
                "Open",
                "Assigned",
                "In Progress",
                "Waiting Dispatch",
                "Closed");
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilterEvents = true;
            try
            {
                _activeQuickFilter = TicketQuickFilter.None;

                SearchBox.Text = string.Empty;
                DateRangeFilter.SelectedIndex = 0;
                TechFilter.SelectedItem = "All";

                FromDatePicker.SelectedDate = null;
                ToDatePicker.SelectedDate = null;

                SetSelectedStatusesWithoutRefresh(
                    "Needs Review",
                    "Open",
                    "Assigned",
                    "In Progress",
                    "Waiting Dispatch");
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            UpdateCustomDateVisibility();
            OnPropertyChanged(nameof(SelectedStatusesSummary));

            await LoadTicketsFromApiAsync();
        }

        private void TicketsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedTicket = TicketsGrid.SelectedItem as DispatchTicket;

            // Single-click should only select/highlight.
            // It should not open the details pane.
            UpdateDetailsVisibility();
            UpdateTicketListUiState();
        }

        private void TicketsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject))
                return;

            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.Item is not DispatchTicket ticket)
                return;

            TicketsGrid.SelectedItem = ticket;
            SelectedTicket = ticket;

            _detailsOpen = true;
            UpdateDetailsVisibility();
        }

        private async void CloseDetails_Click(object sender, RoutedEventArgs e)
        {
            _detailsOpen = false;

            TicketsGrid.SelectedItem = null;
            SelectedTicket = null;
            _selectedTicketOriginalTechNotes = "";
            _selectedTicketOriginalDispatchNotes = "";
            _selectedTicketSiteNotes.Clear();

            UpdateDetailsVisibility();
            UpdateSaveDetailsButtonState();
            UpdateTicketListUiState();

            await LoadTicketsFromApiAsync();
        }

        private async void CopyDetailValue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not string value || string.IsNullOrWhiteSpace(value))
                return;

            Clipboard.SetText(value);

            var originalContent = button.Content;
            button.Content = "Copied!";
            button.IsEnabled = false;

            await Task.Delay(1200);

            button.Content = originalContent;
            button.IsEnabled = true;
        }

        private async void NewTicket_Click(object sender, RoutedEventArgs e)
        {
            await LoadKnownTechsFromApiAsync();

            var techSuggestions = _knownTechs.OrderBy(x => x).ToList();

            var win = new NewTicketWindow(_ticketsApi, techSuggestions)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            await LoadTicketsFromApiAsync();

            if (win.CreatedTicketId is long id && id > 0)
            {
                var found = _tickets.FirstOrDefault(t => t.Id == id);
                if (found != null)
                {
                    TicketsGrid.SelectedItem = found;
                    TicketsGrid.ScrollIntoView(found);
                }
            }
        }

        private async void ImportSapQueue_Click(object sender, RoutedEventArgs e)
        {
            var win = new SapQueueImportWindow(_ticketsApi)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() == true)
                await LoadTicketsFromApiAsync();
        }

        private void MissingProblems_Click(object sender, RoutedEventArgs e)
        {
            ApplyQuickFilter(
                TicketQuickFilter.MissingProblems,
                "Needs Review",
                "Open",
                "Assigned",
                "In Progress",
                "Waiting Dispatch");
        }

        private void UnassignedTickets_Click(object sender, RoutedEventArgs e)
        {
            ApplyQuickFilter(
                TicketQuickFilter.Unassigned,
                "Needs Review",
                "Open",
                "Assigned",
                "In Progress",
                "Waiting Dispatch");
        }

        private void ReadyToAssign_Click(object sender, RoutedEventArgs e)
        {
            ApplyQuickFilter(
                TicketQuickFilter.ReadyToAssign,
                "Open");
        }

        private void AssignedTickets_Click(object sender, RoutedEventArgs e)
        {
            ApplyQuickFilter(
                TicketQuickFilter.Assigned,
                "Assigned");
        }

        private async void ClearQuickFilter_Click(object sender, RoutedEventArgs e)
        {
            _activeQuickFilter = TicketQuickFilter.None;

            _suppressFilterEvents = true;
            try
            {
                SearchBox.Text = string.Empty;
                DateRangeFilter.SelectedIndex = 0;
                TechFilter.SelectedItem = "All";

                FromDatePicker.SelectedDate = null;
                ToDatePicker.SelectedDate = null;

                SetSelectedStatusesWithoutRefresh(
                    "Needs Review",
                    "Open",
                    "Assigned",
                    "In Progress",
                    "Waiting Dispatch");
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            UpdateCustomDateVisibility();
            OnPropertyChanged(nameof(SelectedStatusesSummary));

            await LoadTicketsFromApiAsync();
        }

        private void SelectVisibleTickets_Click(object sender, RoutedEventArgs e)
        {
            TicketsGrid.SelectedItems.Clear();

            foreach (var ticket in TicketsView.Cast<DispatchTicket>())
                TicketsGrid.SelectedItems.Add(ticket);

            UpdateTicketListUiState();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            TicketsGrid.SelectedItems.Clear();
            SelectedTicket = null;
            UpdateDetailsVisibility();
            UpdateTicketListUiState();
        }

        private async void ApplyQuickFilter(TicketQuickFilter quickFilter, params string[] statusesToShow)
        {
            _activeQuickFilter = quickFilter;

            _suppressFilterEvents = true;
            try
            {
                SearchBox.Text = string.Empty;
                DateRangeFilter.SelectedIndex = 0;
                TechFilter.SelectedItem = "All";

                FromDatePicker.SelectedDate = null;
                ToDatePicker.SelectedDate = null;

                SetSelectedStatusesWithoutRefresh(statusesToShow);
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            UpdateCustomDateVisibility();
            OnPropertyChanged(nameof(SelectedStatusesSummary));

            await LoadTicketsFromApiAsync();
        }

        private List<DispatchTicket> GetSelectedTickets()
        {
            return TicketsGrid.SelectedItems
                .OfType<DispatchTicket>()
                .ToList();
        }

        private async void SaveDetails_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTicket == null)
                return;

            var selectedId = SelectedTicket.Id;

            var req = new UpdateTicketRequest(
                Site: SelectedTicket.Site ?? "",
                NotificationName: SelectedTicket.NotificationName ?? "",
                Notification: SelectedTicket.Notification ?? "",
                WorkOrder: string.IsNullOrWhiteSpace(SelectedTicket.CurrentWorkOrder) ? null : SelectedTicket.CurrentWorkOrder,
                WorkOrderClass: SelectedTicket.WorkOrderType ?? "",
                GroupCode: SelectedTicket.GroupCode ?? "",
                PriorityDays: SelectedTicket.PriorityDays,
                Status: SelectedTicket.Status ?? "",
                TaskCategoryId: SelectedTicket.TaskCategoryId,
                ActionRequiredOverride: string.IsNullOrWhiteSpace(SelectedTicket.ActionRequiredOverride)
                    ? null
                    : SelectedTicket.ActionRequiredOverride,
                AssignedTech: SelectedTicket.AssignedTech ?? "(Unassigned)",
                Problem: SelectedTicket.Problem ?? "",
                Notes: SelectedTicket.Notes ?? "",
                DispatchNotes: SelectedTicket.DispatchNotes ?? ""
            );

            SaveTicketButton.IsEnabled = false;
            EditTicketButton.IsEnabled = false;

            try
            {
                await _ticketsApi.UpdateTicketAsync(selectedId, req);
                await LoadTicketsFromApiAsync();

                var found = _tickets.FirstOrDefault(t => t.Id == selectedId);
                if (found != null)
                {
                    TicketsGrid.SelectedItem = found;
                    TicketsGrid.ScrollIntoView(found);
                }

                _selectedTicketOriginalTechNotes = found?.Notes ?? "";
                _selectedTicketOriginalDispatchNotes = found?.DispatchNotes ?? "";
                UpdateSaveDetailsButtonState();
            }
            catch (ApiClient.ApiException ex) when (ex.StatusCode == 400)
            {
                MessageBox.Show(
                    ex.Body ?? "Request was invalid.",
                    "Save Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (ApiClient.ApiException ex) when (ex.StatusCode == 409)
            {
                MessageBox.Show(
                    ex.Body ?? "A ticket already exists with that Notification #.",
                    "Save Ticket",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Save Ticket Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                EditTicketButton.IsEnabled = true;
                UpdateSaveDetailsButtonState();
            }
        }

        private async void EditTicket_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTicket == null)
                return;

            var editingId = SelectedTicket.Id;

            await LoadKnownTechsFromApiAsync();

            var techSuggestions = _knownTechs.OrderBy(x => x).ToList();

            var win = new NewTicketWindow(_ticketsApi, techSuggestions, SelectedTicket)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            await LoadTicketsFromApiAsync();

            var targetId = win.CreatedTicketId ?? editingId;
            var found = _tickets.FirstOrDefault(t => t.Id == targetId);
            if (found != null)
            {
                TicketsGrid.SelectedItem = found;
                TicketsGrid.ScrollIntoView(found);
            }
        }

        private void UpdateSaveDetailsButtonState()
        {
            if (SaveTicketButton == null)
                return;

            if (SelectedTicket == null || IsSelectedTicketClosed)
            {
                SaveTicketButton.IsEnabled = false;
                return;
            }

            var currentTechNotes = SelectedTicket.Notes ?? "";
            var currentDispatchNotes = SelectedTicket.DispatchNotes ?? "";

            var techNotesChanged =
                !string.Equals(currentTechNotes, _selectedTicketOriginalTechNotes, StringComparison.Ordinal);

            var dispatchNotesChanged =
                !string.Equals(currentDispatchNotes, _selectedTicketOriginalDispatchNotes, StringComparison.Ordinal);

            SaveTicketButton.IsEnabled = techNotesChanged || dispatchNotesChanged;
        }

        private void DetailsDispatchNotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSaveDetailsButtonState();
        }

        private void DetailsTechWriteUpsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSaveDetailsButtonState();
        }

        private void DetailsNotesTextChangedRefresh()
        {
            UpdateSaveDetailsButtonState();
        }

        private void DetailsNotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            DetailsNotesTextChangedRefresh();
        }

        private async void SummaryToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSummaryLoading)
                return;

            if (_isSummaryExpanded)
            {
                SummaryPanel.Visibility = Visibility.Collapsed;
                SummaryToggleButton.Content = "Summary";
                _isSummaryExpanded = false;
                return;
            }

            _isSummaryLoading = true;
            SummaryToggleButton.IsEnabled = false;

            try
            {
                await LoadSummaryFromApiAsync();
                SummaryPanel.Visibility = Visibility.Visible;
                SummaryToggleButton.Content = "Collapse Summary";
                _isSummaryExpanded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load ticket summary.\n\n{ex.Message}",
                    "Summary",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isSummaryLoading = false;
                SummaryToggleButton.IsEnabled = true;
            }
        }

        private async void BulkSetProblem_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedTickets();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more tickets first.",
                    "Set Problem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var win = new BulkSetProblemWindow(selected.Count)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            var problem = win.Problem;
            var updated = 0;
            var failed = 0;
            var selectedIds = selected.Select(t => t.Id).ToHashSet();

            BulkSetProblemButton.IsEnabled = false;
            AssignSelectedButton.IsEnabled = false;
            SelectVisibleTicketsButton.IsEnabled = false;
            ClearSelectionButton.IsEnabled = false;

            try
            {
                foreach (var ticket in selected)
                {
                    try
                    {
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
                            Problem: problem,
                            Notes: ticket.Notes ?? "",
                            DispatchNotes: ticket.DispatchNotes ?? ""
                        );

                        await _ticketsApi.UpdateTicketAsync(ticket.Id, req);
                        updated++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                await LoadTicketsFromApiAsync();

                if (_activeQuickFilter != TicketQuickFilter.MissingProblems)
                {
                    foreach (var ticket in _tickets.Where(t => selectedIds.Contains(t.Id)))
                        TicketsGrid.SelectedItems.Add(ticket);

                    var first = _tickets.FirstOrDefault(t => selectedIds.Contains(t.Id));
                    if (first != null)
                    {
                        TicketsGrid.SelectedItem = first;
                        TicketsGrid.ScrollIntoView(first);
                    }
                }
                else
                {
                    TicketsGrid.SelectedItems.Clear();
                    SelectedTicket = null;
                    UpdateDetailsVisibility();
                }

                var message = failed == 0
                    ? _activeQuickFilter == TicketQuickFilter.MissingProblems
                    ? $"Updated {updated} ticket(s). They were removed from the Missing Problems view."
                        : $"Updated {updated} ticket(s)."
                        : $"Updated {updated} ticket(s). Failed to update {failed}.";

                MessageBox.Show(
                    message,
                    "Set Problem",
                    MessageBoxButton.OK,
                    failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            finally
            {
                UpdateTicketListUiState();
            }
        }

        private async void AssignSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedTickets();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more tickets first.",
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await LoadKnownTechsFromApiAsync();

            var win = new AssignTicketsWindow(selected.Count, _knownTechs)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            var assignedTech = win.AssignedTech;
            var updated = 0;
            var failed = 0;
            var selectedIds = selected.Select(t => t.Id).ToHashSet();

            BulkSetProblemButton.IsEnabled = false;
            AssignSelectedButton.IsEnabled = false;
            SelectVisibleTicketsButton.IsEnabled = false;
            ClearSelectionButton.IsEnabled = false;

            try
            {
                foreach (var ticket in selected)
                {
                    try
                    {
                        var req = new UpdateTicketRequest(
                            Site: ticket.Site ?? "",
                            NotificationName: ticket.NotificationName ?? "",
                            Notification: ticket.Notification ?? "",
                            WorkOrder: string.IsNullOrWhiteSpace(ticket.CurrentWorkOrder) ? null : ticket.CurrentWorkOrder,
                            WorkOrderClass: ticket.WorkOrderType ?? "",
                            GroupCode: ticket.GroupCode ?? "",
                            PriorityDays: ticket.PriorityDays,
                            Status: "Assigned",
                            TaskCategoryId: ticket.TaskCategoryId,
                            ActionRequiredOverride: string.IsNullOrWhiteSpace(ticket.ActionRequiredOverride)
                                ? null
                                : ticket.ActionRequiredOverride,
                            AssignedTech: assignedTech,
                            Problem: ticket.Problem ?? "",
                            Notes: ticket.Notes ?? ""
                        );

                        await _ticketsApi.UpdateTicketAsync(ticket.Id, req);
                        updated++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                await LoadTicketsFromApiAsync();

                foreach (var ticket in _tickets.Where(t => selectedIds.Contains(t.Id)))
                    TicketsGrid.SelectedItems.Add(ticket);

                var first = _tickets.FirstOrDefault(t => selectedIds.Contains(t.Id));
                if (first != null)
                {
                    TicketsGrid.SelectedItem = first;
                    TicketsGrid.ScrollIntoView(first);
                }

                var message = failed == 0
                    ? $"Assigned {updated} ticket(s) to {assignedTech}."
                    : $"Assigned {updated} ticket(s) to {assignedTech}. Failed to update {failed}.";

                MessageBox.Show(
                    message,
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            finally
            {
                UpdateTicketListUiState();
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

        private async Task LoadSiteNotesForSelectedTicketAsync()
        {
            _selectedTicketSiteNotes.Clear();
            OnPropertyChanged(nameof(SelectedTicketSiteNotesCount));

            var site = SelectedTicket?.Site?.Trim();

            if (string.IsNullOrWhiteSpace(site))
                return;

            try
            {
                var notes = await _siteNotesApi.GetBySiteAsync(site);

                foreach (var note in notes)
                    _selectedTicketSiteNotes.Add(note);

                OnPropertyChanged(nameof(SelectedTicketSiteNotesCount));
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

        private void CollapseTicketDetailExpanders()
        {
            if (SiteNotesExpander != null)
                SiteNotesExpander.IsExpanded = false;

            if (DispatchNotesExpander != null)
                DispatchNotesExpander.IsExpanded = false;

            if (TechWriteUpsExpander != null)
                TechWriteUpsExpander.IsExpanded = false;

            UpdateTicketDetailTextBoxHeights();
        }

        private void TicketDetailExpander_ExpandedChanged(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(UpdateTicketDetailTextBoxHeights),
                DispatcherPriority.Background);
        }

        private void UpdateTicketDetailTextBoxHeights()
        {
            if (DetailsDispatchNotesTextBox == null || DetailsTechWriteUpsTextBox == null)
                return;

            var expandedCount = 0;

            if (SiteNotesExpander?.IsExpanded == true)
                expandedCount++;

            if (DispatchNotesExpander?.IsExpanded == true)
                expandedCount++;

            if (TechWriteUpsExpander?.IsExpanded == true)
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

            DetailsDispatchNotesTextBox.Height =
                DispatchNotesExpander?.IsExpanded == true
                    ? dispatchHeight
                    : double.NaN;

            DetailsTechWriteUpsTextBox.Height =
                TechWriteUpsExpander?.IsExpanded == true
                    ? techHeight
                    : double.NaN;
        }
    }
}