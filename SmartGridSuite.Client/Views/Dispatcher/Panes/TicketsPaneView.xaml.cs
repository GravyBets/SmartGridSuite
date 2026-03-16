using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using SmartGridSuite.Contracts.Tickets;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
        private readonly HashSet<string> _knownTechs = new(StringComparer.OrdinalIgnoreCase);

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
                OnPropertyChanged(nameof(SelectedTicket));
            }
        }

        private bool _suppressFilterEvents;

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
            DataContext = this;

            TicketsView = CollectionViewSource.GetDefaultView(_tickets);
            TicketsView.Filter = FilterTicket;
            TicketsView.SortDescriptions.Clear();
            TicketsView.SortDescriptions.Add(
                new SortDescription(nameof(DispatchTicket.LastActivityAt), ListSortDirection.Descending));

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

            Loaded += async (_, __) => await LoadTicketsFromApiAsync();
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

                TotalLoadedTicketCount = _tickets.Count;

                RebuildTechFilterFromLoadedTickets();
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

        private void RebuildTechFilterFromLoadedTickets()
        {
            foreach (var t in _tickets)
            {
                if (!string.IsNullOrWhiteSpace(t.AssignedTech) && t.AssignedTech != "(Unassigned)")
                    _knownTechs.Add(t.AssignedTech);
            }

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

        private static DispatchTicket Map(TicketListItemDto dto)
        {
            var woc = (dto.WorkOrderClass ?? "").Trim();

            var woClass =
                woc.Equals("Cap", StringComparison.OrdinalIgnoreCase) ||
                woc.Equals("Capital", StringComparison.OrdinalIgnoreCase)
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
                WoClass = woClass,
                GroupCode = dto.GroupCode ?? "",
                PriorityDays = dto.PriorityDays,
                Problem = dto.Problem ?? "",
                Notes = dto.Notes ?? "",
                CreatedBy = dto.CreatedBy ?? "",
                Summary = dto.Problem ?? ""
            };
        }

        private HashSet<string> GetSelectedStatuses()
        {
            return StatusOptions
                .Where(x => x.IsSelected)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private (DateTime? from, DateTime? to) GetCreatedDateRangeFromUi()
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

            // Created date range
            var (from, to) = GetCreatedDateRangeFromUi();
            var createdDate = t.CreatedAt;

            if (DateRangeFilter?.SelectedItem as string == "Custom")
            {
                if (from.HasValue && createdDate < from.Value.Date)
                    return false;

                if (to.HasValue && createdDate >= to.Value.Date.AddDays(1))
                    return false;
            }
            else
            {
                if (from.HasValue && createdDate < from.Value)
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
            UpdateSummaryCounts();
        }

        private void UpdateSummaryCounts()
        {
            var visible = TicketsView.Cast<DispatchTicket>().ToList();

            VisibleTicketCount = visible.Count;
            NeedsReviewCount = visible.Count(x => string.Equals(x.Status, "Needs Review", StringComparison.OrdinalIgnoreCase));
            OpenCount = visible.Count(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase));
            AssignedCount = visible.Count(x => string.Equals(x.Status, "Assigned", StringComparison.OrdinalIgnoreCase));
            InProgressCount = visible.Count(x => string.Equals(x.Status, "In Progress", StringComparison.OrdinalIgnoreCase));
            WaitingDispatchCount = visible.Count(x => string.Equals(x.Status, "Waiting Dispatch", StringComparison.OrdinalIgnoreCase));
            ClosedCount = visible.Count(x => string.Equals(x.Status, "Closed", StringComparison.OrdinalIgnoreCase));
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
                DetailsCol.Width = new GridLength(500);
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
            RefreshView();
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

        private void CloseDetails_Click(object sender, RoutedEventArgs e)
        {
            TicketsGrid.SelectedItem = null;
            SelectedTicket = null;
            UpdateDetailsVisibility();
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

        private void AssignTech_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTicket == null) return;
            MessageBox.Show("Assign Tech (later: open assign dialog + set AssignedTech/Status).", "Assign Tech");
        }

        private void EditTicket_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Edit Ticket (coming next).", "Edit Ticket");
        }

        private void AddNote_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTicket == null) return;
            MessageBox.Show("Add Note (later: POST /visits/{id}/notes and bump LastActivityAt).", "Add Note");
        }
    }
}