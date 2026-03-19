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
        private string _selectedTicketOriginalNotes = "";
        private bool _isSummaryExpanded;
        private bool _isSummaryLoading;

        private readonly DispatcherTimer _searchDebounceTimer;

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
                _selectedTicketOriginalNotes = value?.Notes ?? "";

                OnPropertyChanged(nameof(SelectedTicket));
                OnPropertyChanged(nameof(SelectedTicketCreatedByDisplay));

                UpdateSaveDetailsButtonState();
            }
        }
        public string SelectedTicketCreatedByDisplay
                    => ResolveCreatedByDisplay(SelectedTicket?.CreatedBy);

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
            TicketsView.Filter = FilterTicket;
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

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            RefreshView();
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

                    if (!string.IsNullOrWhiteSpace(displayName))
                        _knownTechs.Add(displayName);

                    if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(displayName))
                        _createdByDisplayByUserId[userId] = displayName;
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
            try
            {
                // Load the dataset, then do all UI filtering locally.
                // This makes the multi-status filter and created-date filter behave consistently.
                var dtos = await _ticketsApi.GetTicketsAsync(
                    status: null,
                    tech: null,
                    from: null,
                    to: null,
                    ct);

                _tickets.Clear();
                foreach (var dto in dtos)
                    _tickets.Add(Map(dto));
                                
                RefreshView();
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

            var woClass =
                rawWorkOrderType.Equals("Cap", StringComparison.OrdinalIgnoreCase) ||
                rawWorkOrderType.Equals("Capital", StringComparison.OrdinalIgnoreCase)
                    ? WorkOrderClass.Capital
                    : WorkOrderClass.Maintenance;

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
                CreatedBy = dto.CreatedBy ?? "",
                Summary = dto.Problem ?? "",
                TaskCategoryId = dto.TaskCategoryId,
                TaskCategoryName = dto.TaskCategoryName ?? "",
                ActionRequiredOverride = dto.ActionRequiredOverride ?? ""
            };
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

            return key;
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
                   Match(t.CreatedBy);
        }

        private void RefreshView()
        {
            TicketsView?.Refresh();
            UpdateVisibleTicketCount();
        }

        private void UpdateVisibleTicketCount()
        {
            VisibleTicketCount = TicketsView.Cast<DispatchTicket>().Count();
        }

        private void SetSelectedStatuses(params string[] statusNames)
        {
            var selected = new HashSet<string>(statusNames, StringComparer.OrdinalIgnoreCase);

            foreach (var option in StatusOptions)
                option.IsSelected = selected.Contains(option.Name);

            OnPropertyChanged(nameof(SelectedStatusesSummary));
            RefreshView();
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
            if (SelectedTicket == null)
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
                DetailsSplitter.Visibility = Visibility.Collapsed;
                DetailsSplitterCol.Width = new GridLength(0);
                DetailsCol.Width = new GridLength(0);
            }
            else
            {
                DetailsSplitterCol.Width = new GridLength(10);
                DetailsCol.Width = new GridLength(440);
                DetailsSplitter.Visibility = Visibility.Visible;
                DetailsPanel.Visibility = Visibility.Visible;
            }
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

        private void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents)
                return;

            UpdateCustomDateVisibility();
            RefreshView();
        }

        private void InlineCustomDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents)
                return;

            if ((DateRangeFilter?.SelectedItem as string) != "Custom")
                return;

            RefreshView();
        }

        private void StatusFilterButton_Click(object sender, RoutedEventArgs e)
        {
            StatusPopup.IsOpen = !StatusPopup.IsOpen;
        }

        private void StatusOption_Changed(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged(nameof(SelectedStatusesSummary));
            RefreshView();
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

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilterEvents = true;
            try
            {
                SearchBox.Text = string.Empty;
                DateRangeFilter.SelectedIndex = 0;
                TechFilter.SelectedItem = "All";

                FromDatePicker.SelectedDate = null;
                ToDatePicker.SelectedDate = null;

                SetSelectedStatuses(
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
            RefreshView();
        }

        private void TicketsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedTicket = TicketsGrid.SelectedItem as DispatchTicket;
            UpdateDetailsVisibility();
        }

        private async void CloseDetails_Click(object sender, RoutedEventArgs e)
        {
            TicketsGrid.SelectedItem = null;
            SelectedTicket = null;
            _selectedTicketOriginalNotes = "";
            UpdateDetailsVisibility();
            UpdateSaveDetailsButtonState();
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
                Notes: SelectedTicket.Notes ?? ""
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

                _selectedTicketOriginalNotes = found?.Notes ?? "";
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

            if (SelectedTicket == null)
            {
                SaveTicketButton.IsEnabled = false;
                return;
            }

            var currentNotes = SelectedTicket.Notes ?? "";
            SaveTicketButton.IsEnabled = !string.Equals(currentNotes, _selectedTicketOriginalNotes, StringComparison.Ordinal);
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
    }
}