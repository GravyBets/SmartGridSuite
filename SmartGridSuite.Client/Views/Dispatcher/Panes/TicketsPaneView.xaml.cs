using Accessibility;
using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using System.Threading;
using System.Threading.Tasks;



namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TicketsPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ObservableCollection<DispatchTicket> _tickets = new();
        public ICollectionView TicketsView { get; }

        private CancellationTokenSource? _reloadCts;

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
        private readonly TicketsApi _ticketsApi =
            new TicketsApi(new ApiClient("https://localhost:7140/"));
        private readonly HashSet<string> _knownTechs = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressFilterEvents;

        private void RebuildTechFilterFromLoadedTickets()
        {
            // Add techs from the loaded tickets into a stable set
            foreach (var t in _tickets)
            {
                if (!string.IsNullOrWhiteSpace(t.AssignedTech) && t.AssignedTech != "(Unassigned)")
                    _knownTechs.Add(t.AssignedTech);
            }

            var prev = TechFilter.SelectedItem as string ?? "All";

            var items = new List<string> { "All", "(Unassigned)" };
            items.AddRange(_knownTechs.OrderBy(x => x));

            // Preserve the current selection even if it isn't in the set yet
            if (!string.IsNullOrWhiteSpace(prev) && !items.Contains(prev))
                items.Insert(2, prev);

            _suppressFilterEvents = true;
            try
            {
                TechFilter.ItemsSource = items;
                TechFilter.SelectedItem = items.Contains(prev) ? prev : "All";
            }
            finally
            {
                _suppressFilterEvents = false;
            }
        }

        public TicketsPaneView()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += async (_, __) => await LoadTicketsFromApiAsync();

            TicketsView = CollectionViewSource.GetDefaultView(_tickets);
            TicketsView.Filter = FilterTicket;

            // newest activity first
            TicketsView.SortDescriptions.Clear();
            TicketsView.SortDescriptions.Add(new SortDescription(nameof(DispatchTicket.LastActivityAt), ListSortDirection.Descending));

            TicketsGrid.ItemsSource = TicketsView;

            StatusFilter.ItemsSource = new[] { "All", "Open", "Assigned", "In Progress", "Waiting Dispatch", "Closed" };
            StatusFilter.SelectedIndex = 0;

            DateRangeFilter.ItemsSource = new[]
            {
                "All",
                "Last 24 Hours",
                "Last 7 Days",
                "Last 30 Days",
                "Last 3 Months",
                "Custom"
            };
            DateRangeFilter.SelectedIndex = 0; // Change from 0-5
            UpdateCustomDateVisibility();

            // Tech filter from the data (plus All/Unassigned)
            var techs = _tickets.Select(t => t.AssignedTech)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Distinct()
                                .OrderBy(s => s)
                                .ToList();
            techs.Insert(0, "All");
            techs.Insert(1, "(Unassigned)");
            TechFilter.ItemsSource = techs;
            TechFilter.SelectedIndex = 0;
        }

        // 9 digits: 1405xxxxx / 1214xxxxx

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTicketsFromApiAsync();
        }

        private void ScheduleApiReload(int delayMs = 250)
        {
            if (_suppressFilterEvents) return;

            _reloadCts?.Cancel();
            _reloadCts = new CancellationTokenSource();
            var token = _reloadCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token);
                    await Dispatcher.InvokeAsync(async () => await LoadTicketsFromApiAsync(token));
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private (DateTime? from, DateTime? to) GetDateRangeFromUi()
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

        private async Task LoadTicketsFromApiAsync(CancellationToken ct = default)
        {
            try
            {
                var status = StatusFilter?.SelectedItem as string;
                if (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) status = null;

                var tech = TechFilter?.SelectedItem as string;
                if (string.Equals(tech, "All", StringComparison.OrdinalIgnoreCase)) tech = null;

                var (from, to) = GetDateRangeFromUi();

                var dtos = await _ticketsApi.GetTicketsAsync(status: status, tech: tech, from: from, to: to, ct);

                _tickets.Clear();
                foreach (var dto in dtos)
                    _tickets.Add(Map(dto));

                RebuildTechFilterFromLoadedTickets();
                RefreshView(); // keeps SearchBox local filtering working
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to load tickets from API.\n\n{ex.Message}",
                    "API Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            RebuildTechFilterFromLoadedTickets();
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

                // keep this for now so any existing UI binding still works
                Summary = dto.Problem ?? ""
            };
        }

        private bool FilterTicket(object obj)
        {
            if (obj is not DispatchTicket t) return false;

            // Date Range (uses LastActivityAt)
            var dateRange = DateRangeFilter?.SelectedItem as string ?? "All";

            DateTime? start = dateRange switch
            {
                "Last 24 Hours" => DateTime.Now.AddHours(-24),
                "Last 7 Days" => DateTime.Now.AddDays(-7),
                "Last 30 Days" => DateTime.Now.AddDays(-30),
                "Last 3 Months" => DateTime.Now.AddMonths(-3),
                _ => null
            };

            DateTime ticketDate = t.LastActivityAt; // switch to t.CreatedAt if you prefer

            if (dateRange == "Custom")
            {
                var from = FromDatePicker.SelectedDate; // Date only
                var to = ToDatePicker.SelectedDate;

                if (from.HasValue)
                {
                    var fromStart = from.Value.Date;
                    if (ticketDate < fromStart) return false;
                }

                if (to.HasValue)
                {
                    // inclusive end date
                    var toEndExclusive = to.Value.Date.AddDays(1);
                    if (ticketDate >= toEndExclusive) return false;
                }
            }
            else
            {
                if (start.HasValue && ticketDate < start.Value)
                    return false;
            }

            

            // Status
            var status = StatusFilter?.SelectedItem as string ?? "All";
            if (status != "All" && !string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase))
                return false;

            // Tech
            var tech = TechFilter?.SelectedItem as string ?? "All";
            if (tech == "(Unassigned)" && t.AssignedTech != "(Unassigned)")
                return false;
            if (tech != "All" && tech != "(Unassigned)" &&
                !string.Equals(t.AssignedTech, tech, StringComparison.OrdinalIgnoreCase))
                return false;

            // Search
            var q = (SearchBox?.Text ?? "").Trim();
            if (q.Length == 0) return true;

            bool match(string? s) => !string.IsNullOrWhiteSpace(s) &&
                                     s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            return match(t.Site) ||
                   match(t.NotificationName) ||
                   match(t.Notification) ||
                   match(t.CurrentWorkOrder) ||
                   match(t.WorkOrderClassLabel) ||
                   match(t.GroupCode) ||
                   match(t.Status) ||
                   match(t.AssignedTech) ||
                   match(t.Problem) ||
                   match(t.Summary) ||
                   match(t.Notes) ||
                   match(t.CreatedBy);
        }

        private void RefreshView() => TicketsView?.Refresh();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();

        private void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents) return;

            UpdateCustomDateVisibility();
            ScheduleApiReload();
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

        private void CopyNotification_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && !string.IsNullOrWhiteSpace(s))
                Clipboard.SetText(s);
        }

        private void CopyWorkOrder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string s && !string.IsNullOrWhiteSpace(s))
                Clipboard.SetText(s);
        }

        private async void NewTicket_Click(object sender, RoutedEventArgs e)
        {
            var techSuggestions = _knownTechs.OrderBy(x => x).ToList();

            var win = new NewTicketWindow(_ticketsApi, techSuggestions)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true) return;

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

        private void AssignTech_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTicket == null) return;
            MessageBox.Show("Assign Tech (later: open assign dialog + set AssignedTech/Status).", "Assign Tech");
        }

        private void EditTicket_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Edit Ticket (coming next).", "Edit Ticket");
        }

        private void AddNote_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTicket == null) return;
            MessageBox.Show("Add Note (later: POST /visits/{id}/notes and bump LastActivityAt).", "Add Note");
        }


        private void UpdateCustomDateVisibility()
        {
            var sel = DateRangeFilter?.SelectedItem as string ?? "All";
            bool isCustom = sel == "Custom";

            var spacerWidth = isCustom ? new GridLength(12) : new GridLength(0);
            var dateWidth = isCustom ? new GridLength(140) : new GridLength(0);

            InlineDateSpacer1Col.Width = spacerWidth;
            InlineFromCol.Width = dateWidth;
            InlineDateSpacer2Col.Width = spacerWidth;
            InlineToCol.Width = dateWidth;
            InlineDateSpacer3Col.Width = spacerWidth;

            InlineDateSpacer1Col2.Width = spacerWidth;
            InlineFromCol2.Width = dateWidth;
            InlineDateSpacer2Col2.Width = spacerWidth;
            InlineToCol2.Width = dateWidth;
            InlineDateSpacer3Col2.Width = spacerWidth;

            FromDateLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ToDateLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            FromDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ToDatePicker.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            if (isCustom && FromDatePicker.SelectedDate == null && ToDatePicker.SelectedDate == null)
            {
                ToDatePicker.SelectedDate = DateTime.Today;
                FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
            }
        }

        private void InlineCustomDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents) return;

            var sel = DateRangeFilter?.SelectedItem as string ?? "All";
            if (sel != "Custom")
                return;

            ScheduleApiReload();
        }


    }
    
}