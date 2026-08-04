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

            public bool IsClosed { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(
                        this,
                        new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public StatusFilterOption(string name, bool isClosed, bool isSelected)
            {
                Name = name;
                IsClosed = isClosed;
                _isSelected = isSelected;
            }
        }

        private readonly ObservableCollection<DispatchTicket> _tickets = new();
        private readonly TicketsApi _ticketsApi = new TicketsApi(ClientAppSettings.CreateApiClient());
        private readonly TechniciansApi _techniciansApi;        
        private readonly HashSet<string> _knownTechs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _createdByDisplayByUserId = new(StringComparer.OrdinalIgnoreCase);
        private string _selectedTicketOriginalDispatchNotes = "";

        private int _busyOverlayDepth;

        private readonly SiteNotesApi _siteNotesApi = new(ClientAppSettings.CreateApiClient());

        private readonly ObservableCollection<SiteNoteDto> _selectedTicketSiteNotes = new();
        public int SelectedTicketSiteNotesCount => _selectedTicketSiteNotes.Count;

        private string _selectedTicketOriginalTechNotes = "";

        private enum TicketQuickFilter
        {
            None,
            MissingProblems,
            MissingWorkOrderType,
            Unassigned,
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
        private bool _filtersInitialized;

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

        public ObservableCollection<TicketSummaryStatusDto> SummaryStatuses { get; } = new();

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
                var selected = StatusOptions
                    .Where(x => x.IsSelected)
                    .ToList();

                if (selected.Count == 0)
                    return "No statuses";

                if (selected.Count == StatusOptions.Count)
                    return "All statuses";

                var nonClosedStatuses = StatusOptions
                    .Where(x => !x.IsClosed)
                    .ToList();

                var allNonClosedSelected =
                    nonClosedStatuses.Count > 0 &&
                    nonClosedStatuses.All(x => x.IsSelected) &&
                    StatusOptions.Where(x => x.IsClosed).All(x => !x.IsSelected);

                if (allNonClosedSelected)
                    return "All Active";

                if (selected.Count <= 2)
                    return string.Join(", ", selected.Select(x => x.Name));

                return $"{selected.Count} selected";
            }
        }

        private const int TicketPageSize = 500;

        private int _currentTicketPageIndex;
        private int _totalMatchingTicketCount;

        public int CurrentTicketPageNumber =>
            _currentTicketPageIndex + 1;

        public int TotalTicketPageCount =>
            Math.Max(
                1,
                (int)Math.Ceiling(
                    _totalMatchingTicketCount /
                    (double)TicketPageSize));

        public bool CanGoToPreviousTicketPage =>
            _currentTicketPageIndex > 0;

        public bool CanGoToNextTicketPage =>
            _currentTicketPageIndex + 1 <
            TotalTicketPageCount;

        public string TicketPageSummary
        {
            get
            {
                if (_totalMatchingTicketCount == 0)
                    return "No tickets";

                var first =
                    _currentTicketPageIndex *
                    TicketPageSize + 1;

                var last =
                    Math.Min(
                        first + _tickets.Count - 1,
                        _totalMatchingTicketCount);

                return
                    $"Showing {first:N0}–{last:N0} of " +
                    $"{_totalMatchingTicketCount:N0} · " +
                    $"Page {CurrentTicketPageNumber} of " +
                    $"{TotalTicketPageCount}";
            }
        }

        public TicketsPaneView()
        {
            InitializeComponent();
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            DataContext = this;


            TicketsView = CollectionViewSource.GetDefaultView(_tickets);
            
            _techniciansApi = new TechniciansApi(ClientAppSettings.CreateApiClient());

            TicketsGrid.ItemsSource = TicketsView;

            _suppressFilterEvents = true;
            try
            {
                QuickFilterComboBox.ItemsSource = new[]
                {
                    "All Tickets",
                    "Missing Problems",
                    "Missing WO Type",
                    "Unassigned",
                    "Assigned"
                };
                QuickFilterComboBox.SelectedIndex = 0;

                DateFieldFilter.ItemsSource = new[]
                {
                    "Last Activity",
                    "Created Date"
                };
                DateFieldFilter.SelectedIndex = 0;

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

                UpdateCustomDateVisibility();
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            Loaded += TicketsPaneView_Loaded;
        }

        private void RefreshTicketPagingBindings()
        {
            OnPropertyChanged(
                nameof(CurrentTicketPageNumber));

            OnPropertyChanged(
                nameof(TotalTicketPageCount));

            OnPropertyChanged(
                nameof(CanGoToPreviousTicketPage));

            OnPropertyChanged(
                nameof(CanGoToNextTicketPage));

            OnPropertyChanged(
                nameof(TicketPageSummary));
        }

        private async void QuickFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            var selected = QuickFilterComboBox.SelectedItem as string ?? "All Tickets";

            _activeQuickFilter = selected switch
            {
                "Missing Problems" => TicketQuickFilter.MissingProblems,
                "Missing WO Type" => TicketQuickFilter.MissingWorkOrderType,
                "Unassigned" => TicketQuickFilter.Unassigned,
                "Assigned" => TicketQuickFilter.Assigned,
                _ => TicketQuickFilter.None
            };

            await LoadTicketsFromApiAsync(resetPage: true);
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

            if (!_filtersInitialized)
                return;

            /*
             * A debounced search should not disable the search box or cover the
             * pane with the blocking loading overlay.
             */
            var searchHadFocus =
                SearchBox.IsKeyboardFocusWithin;

            var caretIndex =
                SearchBox.CaretIndex;

            await LoadTicketsFromApiAsync(
                resetPage: true,
                showBusyOverlay: false);

            /*
             * Ordinarily focus never leaves because SearchBox was not disabled.
             * Restore the caret only when the user is still typing there; do not
             * steal focus if they clicked another control while the API was loading.
             */
            if (searchHadFocus &&
                SearchBox.IsKeyboardFocusWithin)
            {
                SearchBox.CaretIndex =
                    Math.Min(
                        caretIndex,
                        SearchBox.Text?.Length ?? 0);
            }
        }

        private async void TicketsPaneView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedOnce || _isInitialLoadRunning)
                return;

            _isInitialLoadRunning = true;
            ShowBusyOverlay("Loading tickets, filters, and summary...");

            try
            {
                await LoadKnownTechsFromApiAsync();
                RebuildTechFilterFromKnownTechs();

                await LoadStatusOptionsFromApiAsync(
                    preserveSelections: false);

                _filtersInitialized = true;

                await LoadSummaryFromApiAsync();
                await LoadTicketsFromApiAsync();

                _hasLoadedOnce = true;
            }
            finally
            {
                _isInitialLoadRunning = false;
                HideBusyOverlay();
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

        private async Task LoadTicketsFromApiAsync(bool resetPage = false, CancellationToken ct = default, bool showBusyOverlay = true)
        {
            if (!_filtersInitialized)
                return;

            if (resetPage)
                _currentTicketPageIndex = 0;

            _ticketQueryCts?.Cancel();
            _ticketQueryCts?.Dispose();

            _ticketQueryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var queryCt = _ticketQueryCts.Token;

            if (showBusyOverlay)
            {
                ShowBusyOverlay(
                    "Loading tickets...");
            }

            try
            {
                var req = BuildTicketQueryRequest();

                var response = await _ticketsApi.QueryTicketsAsync(req, queryCt);

                /*
                 * A delete, status change, or filter change could leave the current page
                 * beyond the new final page. Move back to the final valid page and reload.
                 */
                var maximumPageIndex =
                    response.TotalCount <= 0
                        ? 0
                        : (response.TotalCount - 1) /
                          TicketPageSize;

                if (_currentTicketPageIndex >
                    maximumPageIndex)
                {
                    _currentTicketPageIndex =
                        maximumPageIndex;

                    await LoadTicketsFromApiAsync(
                        resetPage: false,
                        ct: ct,
                        showBusyOverlay: showBusyOverlay);

                    return;
                }

                _tickets.Clear();

                foreach (var dto in response.Items)
                {
                    _tickets.Add(
                        Map(dto));
                }

                /*
                 * VisibleTicketCount is the number currently displayed on this page.
                 * TotalLoadedTicketCount remains the total number matching the API query.
                 */
                VisibleTicketCount =
                    response.Items.Count;

                TotalLoadedTicketCount =
                    response.TotalCount;

                _totalMatchingTicketCount =
                    response.TotalCount;

                RefreshTicketPagingBindings();

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
            finally
            {
                if (showBusyOverlay)
                {
                    HideBusyOverlay();
                }
            }
        }

        private TicketQueryRequest BuildTicketQueryRequest()
        {
            var (from, to) = GetLastActivityDateRangeFromUi();

            var selectedDateField = DateFieldFilter?.SelectedItem as string ?? "Last Activity";

            var apiDateField = selectedDateField.Equals("Created Date", StringComparison.OrdinalIgnoreCase)
                ? "Created"
                : "LastActivity";

            return new TicketQueryRequest
            {
                Search = SearchBox?.Text?.Trim(),
                Statuses = GetSelectedStatuses()
                    .OrderBy(x => x)
                    .ToList(),

                ApplyStatusFilter = true,

                AssignedTech = TechFilter?.SelectedItem as string ?? "All",

                DateField = apiDateField,
                From = from,
                To = to,

                QuickFilter = GetActiveQuickFilterApiValue(),

                Skip =
                    _currentTicketPageIndex *
                    TicketPageSize,

                Take =
                    TicketPageSize
            };
        }

        private string? GetActiveQuickFilterApiValue()
        {
            return _activeQuickFilter switch
            {
                TicketQuickFilter.MissingProblems => "MissingProblems",
                TicketQuickFilter.MissingWorkOrderType => "MissingWorkOrderType",
                TicketQuickFilter.Unassigned => "Unassigned",
                TicketQuickFilter.Assigned => "Assigned",
                _ => null
            };
        }

        private async Task LoadSummaryFromApiAsync(CancellationToken ct = default)
        {
            var summary = await _ticketsApi.GetSummaryAsync(ct);

            TotalLoadedTicketCount = summary.TotalCount;

            SummaryStatuses.Clear();

            foreach (var status in summary.Statuses.OrderBy(x => x.SortOrder).ThenBy(x => x.Status))
                SummaryStatuses.Add(status);
        }

        private async Task LoadStatusOptionsFromApiAsync(bool preserveSelections, CancellationToken ct = default)
        {
            var previousSelections = StatusOptions
                .ToDictionary(
                    x => x.Name,
                    x => x.IsSelected,
                    StringComparer.OrdinalIgnoreCase);

            var statuses = await _ticketsApi.GetFilterStatusesAsync(ct);

            var previousSuppress = _suppressFilterEvents;
            _suppressFilterEvents = true;

            try
            {
                StatusOptions.Clear();

                foreach (var status in statuses
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Name))
                {
                    var isSelected =
                        preserveSelections &&
                        previousSelections.TryGetValue(status.Name, out var previousSelection)
                            ? previousSelection
                            : !status.IsClosed;

                    StatusOptions.Add(new StatusFilterOption(
                        status.Name,
                        status.IsClosed,
                        isSelected));
                }
            }
            finally
            {
                _suppressFilterEvents = previousSuppress;
            }

            OnPropertyChanged(nameof(SelectedStatusesSummary));
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

        private void UpdateTicketListUiState()
        {
            var selectedCount = TicketsGrid?.SelectedItems
                .OfType<DispatchTicket>()
                .Count() ?? 0;

            if (HeaderSelectAllCheckBox != null)
            {
                var allShowingSelected =
                    VisibleTicketCount > 0 &&
                    selectedCount >= VisibleTicketCount;

                HeaderSelectAllCheckBox.IsChecked = allShowingSelected;
            }

            var bulkSetEnabled = selectedCount > 0 && !_detailsOpen;

            if (BulkSetProblemButton != null)
                BulkSetProblemButton.IsEnabled = bulkSetEnabled;

            if (BulkSetWorkOrderTypeButton != null)
                BulkSetWorkOrderTypeButton.IsEnabled = bulkSetEnabled;

            if (BulkSetStatusButton != null)
                BulkSetStatusButton.IsEnabled = bulkSetEnabled;

            if (AssignSelectedButton != null)
                AssignSelectedButton.IsEnabled = selectedCount > 0;

            if (CopySelectedWorkOrdersButton != null)
                CopySelectedWorkOrdersButton.IsEnabled =
                    GetSelectedTicketsInVisibleOrder()
                        .Any(x => !string.IsNullOrWhiteSpace(x.CurrentWorkOrder));

            if (EditSelectedTicketButton != null)
                EditSelectedTicketButton.IsEnabled = selectedCount > 0;

            if (OpenDetailsButton != null)
                OpenDetailsButton.IsEnabled = selectedCount > 0;

            if (EditTicketButton != null)
            {
                EditTicketButton.IsEnabled =
                    SelectedTicket is not null &&
                    !IsSelectedTicketClosed;
            }

            if (SelectedCountTextBlock != null)
            {
                SelectedCountTextBlock.Text = selectedCount == 0
                    ? ""
                    : $"{selectedCount} selected";
            }

            var hasQuickFilter = _activeQuickFilter != TicketQuickFilter.None;
            var quickFilterName = GetActiveQuickFilterDisplayName();

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

            if (QuickFilterComboBox != null)
            {
                var expectedText = _activeQuickFilter switch
                {
                    TicketQuickFilter.MissingProblems => "Missing Problems",
                    TicketQuickFilter.MissingWorkOrderType => "Missing WO Type",
                    TicketQuickFilter.Unassigned => "Unassigned",
                    TicketQuickFilter.Assigned => "Assigned",
                    _ => "All Tickets"
                };

                if (!Equals(QuickFilterComboBox.SelectedItem, expectedText))
                {
                    var previousSuppress = _suppressFilterEvents;
                    _suppressFilterEvents = true;

                    try
                    {
                        QuickFilterComboBox.SelectedItem = expectedText;
                    }
                    finally
                    {
                        _suppressFilterEvents = previousSuppress;
                    }
                }
            }
        }

        private string GetActiveQuickFilterDisplayName()
        {
            return _activeQuickFilter switch
            {
                TicketQuickFilter.MissingProblems => "Missing Problems",
                TicketQuickFilter.MissingWorkOrderType => "Missing WO Type",
                TicketQuickFilter.Unassigned => "Unassigned",
                TicketQuickFilter.Assigned => "Assigned",
                _ => ""
            };
        }

        private async void SetSelectedStatuses(params string[] statusNames)
        {
            SetSelectedStatusesWithoutRefresh(statusNames);
            await LoadTicketsFromApiAsync(resetPage: true);
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
            var dateWidth = isCustom ? new GridLength(190) : new GridLength(0);

            CustomFromSpacerCol.Width = spacerWidth;
            CustomFromCol.Width = dateWidth;
            CustomToSpacerCol.Width = spacerWidth;
            CustomToCol.Width = dateWidth;

            FromDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ToDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            if (isCustom && FromDatePicker.SelectedDate == null && ToDatePicker.SelectedDate == null)
            {
                var previousSuppress = _suppressFilterEvents;
                _suppressFilterEvents = true;

                try
                {
                    ToDatePicker.SelectedDate = DateTime.Today;
                    FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
                }
                finally
                {
                    _suppressFilterEvents = previousSuppress;
                }
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
            ShowBusyOverlay("Refreshing tickets, filters, and summary...");

            try
            {
                await LoadStatusOptionsFromApiAsync(
                    preserveSelections: true);

                await LoadSummaryFromApiAsync();
                await LoadTicketsFromApiAsync();
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            /*
             * Do not start an extra search while filters are being initialized
             * or while Clear Filters is changing SearchBox.Text in code.
             */
            if (_suppressFilterEvents ||
                !_filtersInitialized)
            {
                return;
            }

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            UpdateCustomDateVisibility();

            await Dispatcher.InvokeAsync(
                async () =>
                    await LoadTicketsFromApiAsync(
                        resetPage: true),
                DispatcherPriority.Background);
        }

        private async void InlineCustomDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            if ((DateRangeFilter?.SelectedItem as string) != "Custom")
                return;

            await LoadTicketsFromApiAsync(resetPage: true);
        }

        private void StatusFilterButton_Click(object sender, RoutedEventArgs e)
        {
            StatusPopup.IsOpen = !StatusPopup.IsOpen;
        }

        private async void StatusOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressFilterEvents || !_filtersInitialized)
                return;

            OnPropertyChanged(nameof(SelectedStatusesSummary));
            await LoadTicketsFromApiAsync(resetPage: true);
        }

        private void SelectAllOpenStatuses_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedStatuses(
                StatusOptions
                    .Where(x => !x.IsClosed)
                    .Select(x => x.Name)
                    .ToArray());
        }

        private void SelectAllStatuses_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedStatuses(
                StatusOptions
                    .Select(x => x.Name)
                    .ToArray());
        }

        private void SetDefaultStatusSelectionWithoutRefresh()
        {
            foreach (var option in StatusOptions)
                option.IsSelected = !option.IsClosed;

            OnPropertyChanged(nameof(SelectedStatusesSummary));
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilterEvents = true;
            try
            {
                _activeQuickFilter = TicketQuickFilter.None;

                SearchBox.Text = string.Empty;
                DateFieldFilter.SelectedIndex = 0;
                DateRangeFilter.SelectedIndex = 0;
                TechFilter.SelectedItem = "All";
                QuickFilterComboBox.SelectedItem = "All Tickets";

                FromDatePicker.SelectedDate = null;
                ToDatePicker.SelectedDate = null;

                SetDefaultStatusSelectionWithoutRefresh();
            }
            finally
            {
                _suppressFilterEvents = false;
            }

            UpdateCustomDateVisibility();
            OnPropertyChanged(nameof(SelectedStatusesSummary));

            await LoadTicketsFromApiAsync(resetPage: true);
        }

        private async void CopyGridValue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var value = button.Tag?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(value))
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

            e.Handled = true;
        }

        private async void CopySelectedWorkOrders_Click(object sender, RoutedEventArgs e)
        {
            var selectedTickets = GetSelectedTicketsInVisibleOrder();

            if (selectedTickets.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more tickets first.",
                    "Copy Work Orders",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var workOrders = selectedTickets
                .Select(x => (x.CurrentWorkOrder ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (workOrders.Count == 0)
            {
                MessageBox.Show(
                    "None of the selected tickets have a Work Order to copy.",
                    "Copy Work Orders",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, workOrders));

            if (sender is Button button)
            {
                var originalContent = button.Content;
                var originalIsEnabled = button.IsEnabled;

                button.Content = workOrders.Count == 1
                    ? "Copied 1"
                    : $"Copied {workOrders.Count}";

                button.IsEnabled = false;

                await Task.Delay(1800);

                button.Content = originalContent;
                button.IsEnabled = originalIsEnabled;

                UpdateTicketListUiState();
            }
        }

        private void HeaderSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (TicketsGrid == null)
                return;

            if (HeaderSelectAllCheckBox.IsChecked == true)
            {
                TicketsGrid.SelectedItems.Clear();

                foreach (var ticket in TicketsView.Cast<DispatchTicket>())
                    TicketsGrid.SelectedItems.Add(ticket);
            }
            else
            {
                TicketsGrid.SelectedItems.Clear();
                SelectedTicket = null;
                _detailsOpen = false;
                UpdateDetailsVisibility();
            }

            UpdateTicketListUiState();
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
            UpdateTicketListUiState();
        }

        private void OpenDetails_Click(object sender, RoutedEventArgs e)
        {
            var ticketToOpen = GetTopVisibleSelectedTicket();

            if (ticketToOpen == null)
                return;

            TicketsGrid.SelectedItem = ticketToOpen;
            SelectedTicket = ticketToOpen;

            _detailsOpen = true;
            UpdateDetailsVisibility();
            UpdateTicketListUiState();

            TicketsGrid.ScrollIntoView(ticketToOpen);
        }

        private DispatchTicket? GetTopVisibleSelectedTicket()
        {
            if (TicketsGrid?.SelectedItems == null || TicketsGrid.SelectedItems.Count == 0)
                return null;

            var selected = TicketsGrid.SelectedItems
                .OfType<DispatchTicket>()
                .ToHashSet();

            return TicketsView
                .Cast<DispatchTicket>()
                .FirstOrDefault(ticket => selected.Contains(ticket));
        }

        private List<DispatchTicket> GetSelectedTicketsInVisibleOrder()
        {
            if (TicketsGrid?.SelectedItems == null ||
                TicketsGrid.SelectedItems.Count == 0)
            {
                return new List<DispatchTicket>();
            }

            var selected = TicketsGrid.SelectedItems
                .OfType<DispatchTicket>()
                .ToHashSet();

            return TicketsView
                .Cast<DispatchTicket>()
                .Where(ticket => selected.Contains(ticket))
                .ToList();
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

        private async void EditSelectedTicket_Click(object sender, RoutedEventArgs e)
        {
            var ticketToEdit = GetTopVisibleSelectedTicket();

            if (ticketToEdit == null)
                return;

            await OpenTicketEditorAsync(ticketToEdit);
        }

        private async void EditTicket_Click(
            object sender,
            RoutedEventArgs e)
        {
            var ticketToEdit =
                GetTopVisibleSelectedTicket() ??
                SelectedTicket;

            if (ticketToEdit == null)
                return;

            await OpenTicketEditorAsync(ticketToEdit);
        }

        private async Task OpenTicketEditorAsync(DispatchTicket ticketToEdit)
        {
            var editingId = ticketToEdit.Id;

            TicketsGrid.SelectedItem = ticketToEdit;
            SelectedTicket = ticketToEdit;
            TicketsGrid.ScrollIntoView(ticketToEdit);

            await LoadKnownTechsFromApiAsync();

            var techSuggestions = _knownTechs
                .OrderBy(x => x)
                .ToList();

            var win = new NewTicketWindow(_ticketsApi, techSuggestions, ticketToEdit)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            if (win.WasDeleted)
            {
                _detailsOpen = false;

                TicketsGrid.SelectedItems.Clear();

                SelectedTicket = null;

                _selectedTicketOriginalTechNotes = "";
                _selectedTicketOriginalDispatchNotes = "";

                _selectedTicketSiteNotes.Clear();

                UpdateDetailsVisibility();
                UpdateSaveDetailsButtonState();
                UpdateTicketListUiState();

                await LoadSummaryFromApiAsync();
                await LoadTicketsFromApiAsync(
                    resetPage: false);

                return;
            }

            await LoadSummaryFromApiAsync();
            await LoadTicketsFromApiAsync();

            var targetId =
                win.CreatedTicketId ??
                editingId;

            var found = _tickets.FirstOrDefault(t => t.Id == targetId);

            if (found != null)
            {
                TicketsGrid.SelectedItem = found;
                SelectedTicket = found;
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
            var selectedIds = selected
                .Select(t => t.Id)
                .Where(x => x > 0)
                .Distinct()
                .ToHashSet();

            BulkSetProblemButton.IsEnabled = false;
            AssignSelectedButton.IsEnabled = false;

            try
            {
                var req = new BulkSetProblemRequest
                {
                    TicketIds = selectedIds.ToList(),
                    Problem = problem,
                    UpdatedBy = GetCurrentUserDisplayName()
                };

                var result = await _ticketsApi.BulkSetProblemAsync(req);

                await LoadSummaryFromApiAsync();
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

                var updatedCount = result?.UpdatedCount ?? 0;
                var notFoundCount = result?.NotFoundCount ?? 0;

                var message =
                    _activeQuickFilter == TicketQuickFilter.MissingProblems
                        ? $"Updated {updatedCount} ticket(s). They were removed from the Missing Problems view."
                        : $"Updated {updatedCount} ticket(s).";

                if (notFoundCount > 0)
                {
                    message +=
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        $"{notFoundCount} ticket(s) were not found.";
                }

                MessageBox.Show(
                    message,
                    "Set Problem",
                    MessageBoxButton.OK,
                    notFoundCount == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Set Problem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Set Problem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                UpdateTicketListUiState();
            }
        }

        private async void BulkSetStatus_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedTickets();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more tickets first.",
                    "Set Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var statuses = StatusOptions
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var win = new BulkSetStatusWindow(
                selected.Count,
                statuses)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            var selectedStatus =
                win.SelectedStatus;

            if (IsClosedBulkStatus(selectedStatus))
            {
                var confirm = MessageBox.Show(
                    $"Close {selected.Count} selected ticket(s)?\n\n" +
                    "Closed tickets are normally hidden from active dispatcher views. " +
                    "This should only be used when the tickets are fully complete and ready to leave the active queue.",
                    "Confirm Close Tickets",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            var selectedIds = selected
                .Select(t => t.Id)
                .Where(x => x > 0)
                .Distinct()
                .ToHashSet();

            BulkSetProblemButton.IsEnabled = false;
            BulkSetWorkOrderTypeButton.IsEnabled = false;
            BulkSetStatusButton.IsEnabled = false;
            AssignSelectedButton.IsEnabled = false;

            try
            {
                var req = new BulkSetStatusRequest
                {
                    TicketIds = selectedIds.ToList(),
                    Status = selectedStatus,
                    UpdatedBy = GetCurrentUserDisplayName()
                };

                var result =
                    await _ticketsApi.BulkSetStatusAsync(req);

                await LoadSummaryFromApiAsync();
                await LoadTicketsFromApiAsync();

                foreach (var ticket in _tickets.Where(t => selectedIds.Contains(t.Id)))
                    TicketsGrid.SelectedItems.Add(ticket);

                var first = _tickets.FirstOrDefault(t => selectedIds.Contains(t.Id));

                if (first != null)
                {
                    TicketsGrid.SelectedItem = first;
                    TicketsGrid.ScrollIntoView(first);
                }

                var updatedCount = result?.UpdatedCount ?? 0;
                var notFoundCount = result?.NotFoundCount ?? 0;

                var message =
                    IsClosedBulkStatus(selectedStatus)
                        ? $"Closed {updatedCount} ticket(s)."
                        : $"Updated status on {updatedCount} ticket(s).";

                if (notFoundCount > 0)
                {
                    message +=
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        $"{notFoundCount} ticket(s) were not found.";
                }

                MessageBox.Show(
                    message,
                    "Set Status",
                    MessageBoxButton.OK,
                    notFoundCount == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Set Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Set Status",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                UpdateTicketListUiState();
            }
        }

        private async void BulkSetWorkOrderType_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedTickets();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more tickets first.",
                    "Set WO Type",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var win = new BulkSetWorkOrderTypeWindow(selected.Count)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            var selectedIds = selected
                .Select(t => t.Id)
                .Where(x => x > 0)
                .Distinct()
                .ToHashSet();

            BulkSetProblemButton.IsEnabled = false;
            BulkSetWorkOrderTypeButton.IsEnabled = false;
            BulkSetStatusButton.IsEnabled = false;
            AssignSelectedButton.IsEnabled = false;

            try
            {
                var req = new BulkSetWorkOrderTypeRequest
                {
                    TicketIds = selectedIds.ToList(),
                    WorkOrderType = win.WorkOrderType,
                    UpdatedBy = GetCurrentUserDisplayName()
                };

                var result =
                    await _ticketsApi.BulkSetWorkOrderTypeAsync(req);

                await LoadSummaryFromApiAsync();
                await LoadTicketsFromApiAsync();

                foreach (var ticket in _tickets.Where(t => selectedIds.Contains(t.Id)))
                    TicketsGrid.SelectedItems.Add(ticket);

                var first = _tickets.FirstOrDefault(t => selectedIds.Contains(t.Id));

                if (first != null)
                {
                    TicketsGrid.SelectedItem = first;
                    TicketsGrid.ScrollIntoView(first);
                }

                var updatedCount = result?.UpdatedCount ?? 0;
                var skippedCount = result?.SkippedCount ?? 0;
                var notFoundCount = result?.NotFoundCount ?? 0;

                var message =
                    $"Updated WO Type on {updatedCount} ticket(s).";

                if (skippedCount > 0)
                {
                    message +=
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        $"{skippedCount} ticket(s) were skipped because they do not have a Work Order.";
                }

                if (notFoundCount > 0)
                {
                    message +=
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        $"{notFoundCount} ticket(s) were not found.";
                }

                MessageBox.Show(
                    message,
                    "Set WO Type",
                    MessageBoxButton.OK,
                    skippedCount == 0 && notFoundCount == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Set WO Type",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Set WO Type",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

            var techSuggestions = new List<string> { "(Unassigned)" };
            techSuggestions.AddRange(
                _knownTechs
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(x => !x.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x));

            var win = new AssignTicketsWindow(selected.Count, techSuggestions)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            var assignedTech = string.IsNullOrWhiteSpace(win.AssignedTech)
                ? "(Unassigned)"
                : win.AssignedTech.Trim();

            var isUnassigning = assignedTech.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase);

            var selectedIds = selected
                .Select(t => t.Id)
                .Where(x => x > 0)
                .Distinct()
                .ToHashSet();

            BulkSetProblemButton.IsEnabled = false;
            AssignSelectedButton.IsEnabled = false;

            try
            {
                var req = new BulkAssignTicketsRequest
                {
                    TicketIds = selectedIds.ToList(),
                    AssignedTech = assignedTech,
                    UpdatedBy = GetCurrentUserDisplayName()
                };

                var result = await _ticketsApi.BulkAssignTicketsAsync(req);

                await LoadSummaryFromApiAsync();
                await LoadTicketsFromApiAsync();

                foreach (var ticket in _tickets.Where(t => selectedIds.Contains(t.Id)))
                    TicketsGrid.SelectedItems.Add(ticket);

                var first = _tickets.FirstOrDefault(t => selectedIds.Contains(t.Id));

                if (first != null)
                {
                    TicketsGrid.SelectedItem = first;
                    TicketsGrid.ScrollIntoView(first);
                }

                var updatedCount = result?.UpdatedCount ?? 0;
                var notFoundCount = result?.NotFoundCount ?? 0;

                var message =
                    isUnassigning
                        ? $"Unassigned {updatedCount} ticket(s)."
                        : $"Assigned {updatedCount} ticket(s) to {assignedTech}.";

                if (notFoundCount > 0)
                {
                    message +=
                        $"{Environment.NewLine}{Environment.NewLine}" +
                        $"{notFoundCount} ticket(s) were not found.";
                }

                MessageBox.Show(
                    message,
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    notFoundCount == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    ex.Body ?? ex.Message,
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Assign Tickets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                UpdateTicketListUiState();
            }
        }

        private void TicketRowCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not CheckBox checkBox)
                return;

            var row = FindVisualParent<DataGridRow>(checkBox);

            if (row?.Item is not DispatchTicket ticket)
                return;

            if (row.IsSelected)
            {
                TicketsGrid.SelectedItems.Remove(ticket);

                if (ReferenceEquals(SelectedTicket, ticket))
                    SelectedTicket = TicketsGrid.SelectedItems.OfType<DispatchTicket>().FirstOrDefault();
            }
            else
            {
                TicketsGrid.SelectedItems.Add(ticket);
                SelectedTicket = ticket;
            }

            UpdateTicketListUiState();

            e.Handled = true;
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

        private async void PreviousTicketPage_Click(object sender, RoutedEventArgs e)
        {
            if (!CanGoToPreviousTicketPage)
                return;

            _currentTicketPageIndex--;

            await LoadTicketsFromApiAsync();
        }

        private async void NextTicketPage_Click(object sender, RoutedEventArgs e)
        {
            if (!CanGoToNextTicketPage)
                return;

            _currentTicketPageIndex++;

            await LoadTicketsFromApiAsync();
        }

        private bool IsClosedBulkStatus(string statusName)
        {
            if (string.IsNullOrWhiteSpace(statusName))
                return false;

            var configuredStatus = StatusOptions
                .FirstOrDefault(x =>
                    x.Name.Equals(
                        statusName,
                        StringComparison.OrdinalIgnoreCase));

            if (configuredStatus?.IsClosed == true)
                return true;

            return statusName.Equals(
                "Closed",
                StringComparison.OrdinalIgnoreCase);
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
            SetTicketPaneControlsEnabled(false);
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
            SetTicketPaneControlsEnabled(true);

            // Restore action buttons based on current selection after controls unlock.
            UpdateTicketListUiState();
            RefreshTicketPagingBindings();
        }

        private void SetTicketPaneControlsEnabled(bool enabled)
        {
            if (StatusPopup is not null && !enabled)
                StatusPopup.IsOpen = false;

            RefreshTicketsButton.IsEnabled = enabled;
            ImportSapQueueButton.IsEnabled = enabled;
            NewTicketButton.IsEnabled = enabled;
            ClearTicketsFiltersButton.IsEnabled = enabled;

            SearchBox.IsEnabled = enabled;
            DateFieldFilter.IsEnabled = enabled;
            DateRangeFilter.IsEnabled = enabled;
            FromDatePicker.IsEnabled = enabled;
            ToDatePicker.IsEnabled = enabled;
            StatusFilterButton.IsEnabled = enabled;
            TechFilter.IsEnabled = enabled;
            QuickFilterComboBox.IsEnabled = enabled;

            TicketsGrid.IsEnabled = enabled;
            PreviousTicketPageButton.IsEnabled = enabled && CanGoToPreviousTicketPage;
            NextTicketPageButton.IsEnabled = enabled && CanGoToNextTicketPage;

            AssignSelectedButton.IsEnabled = enabled && TicketsGrid.SelectedItems.Count > 0;

            BulkSetProblemButton.IsEnabled = enabled && !_detailsOpen && TicketsGrid.SelectedItems.Count > 0;
            BulkSetWorkOrderTypeButton.IsEnabled = enabled && !_detailsOpen && TicketsGrid.SelectedItems.Count > 0;
            BulkSetStatusButton.IsEnabled = enabled && !_detailsOpen && TicketsGrid.SelectedItems.Count > 0;

            EditSelectedTicketButton.IsEnabled = enabled && TicketsGrid.SelectedItems.Count > 0;
            OpenDetailsButton.IsEnabled = enabled && TicketsGrid.SelectedItems.Count > 0;

            EditTicketButton.IsEnabled = enabled && SelectedTicket is not null;
            SaveTicketButton.IsEnabled = enabled && SaveTicketButton.IsEnabled;
        }
    }
}