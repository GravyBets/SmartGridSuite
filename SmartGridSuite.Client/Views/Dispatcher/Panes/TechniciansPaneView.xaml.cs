#nullable enable
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using SmartGridSuite.Contracts.Administration.Technicians;
using SmartGridSuite.Contracts.Administration.Trucks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TechniciansPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly HttpClient _http;
        private readonly object _queueLock = new();
        private readonly Queue<int> _techQueue = new();
        private readonly HashSet<int> _pendingTechs = new();
        private readonly Dictionary<int, MoveTechnicianRequest> _latestMoveByTech = new();

        private bool _busyLoading;
        private bool _showAllTechnicians;
        private string _technicianSearchText = "";
        private Task? _moveRunner;

        private readonly HashSet<int> _selectedTechnicianIds = new();

        private bool _syncingTechnicianSelection;

        public TruckBoardVm Board { get; private set; } = new();

        public int OnDutyCount => Board.AllTechnicians.Count(t => t.IsOnShift);
        public int OffDutyCount => Board.AllTechnicians.Count(t => !t.IsOnShift);
        public int AssignedCount => Board.Trucks.Sum(t => t.Technicians.Count);
        public int UnassignedCount => Board.AllTechnicians.Count(t => string.IsNullOrWhiteSpace(t.TruckNumber));
        public int TrucksUsedCount => Board.Trucks.Count(t => t.Technicians.Count > 0);

        public int VisibleTechnicianCount
        {
            get
            {
                var view = CollectionViewSource.GetDefaultView(Board.AllTechnicians);
                return view == null ? Board.AllTechnicians.Count : view.Cast<object>().Count();
            }
        }

        public int SelectedDrawerTechnicianCount => _selectedTechnicianIds.Count;

        public bool HasSelectedDrawerTechnicians => _selectedTechnicianIds.Count > 0;

        public TechniciansPaneView()
        {
            InitializeComponent();
            DataContext = this;

            _http = new HttpClient { BaseAddress = new Uri("https://localhost:7140/") };

            Loaded += async (_, __) => await InitializeAndLoadAsync();
        }

        private void SetStatus(string msg)
        {
            // Status is intentionally hidden from the UI.
            // Keep this method so the existing load/save code does not need to change.
            System.Diagnostics.Debug.WriteLine($"Truck Board: {msg}");
        }

        private async Task InitializeAndLoadAsync()
        {
            if (_busyLoading)
                return;

            try
            {
                _busyLoading = true;
                SetStatus("Initializing board...");

                var d = DateTime.Today.ToString("yyyy-MM-dd");
                await _http.PostAsync($"api/trucks/board/initialize?date={d}", content: null);

                await LoadBoardAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _busyLoading = false;
            }
        }

        private async Task LoadBoardAsync()
        {
            try
            {
                SetStatus("Loading board...");

                var d = DateTime.Today.ToString("yyyy-MM-dd");
                var dto = await _http.GetFromJsonAsync<TruckBoardDto>($"api/trucks/board?date={d}");

                Board = TruckBoardVm.FromDto(dto);
                NormalizeTruckAssignments();
                RefreshBoardMetrics();
                RefreshTechnicianFilter();

                SetStatus($"Loaded {Board.AllTechnicians.Count} technicians, {Board.Trucks.Count} trucks.");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
        }

        private void RefreshBoardMetrics()
        {
            OnPropertyChanged(nameof(Board));
            OnPropertyChanged(nameof(OnDutyCount));
            OnPropertyChanged(nameof(OffDutyCount));
            OnPropertyChanged(nameof(AssignedCount));
            OnPropertyChanged(nameof(UnassignedCount));
            OnPropertyChanged(nameof(TrucksUsedCount));
            OnPropertyChanged(nameof(VisibleTechnicianCount));
        }

        private void NormalizeTruckAssignments()
        {
            foreach (var tech in Board.AllTechnicians)
                tech.TruckNumber = null;

            foreach (var truck in Board.Trucks)
            {
                foreach (var tech in truck.Technicians)
                {
                    tech.TruckNumber = truck.Truck.TruckNumber;

                    var allTech = Board.AllTechnicians.FirstOrDefault(x => x.Id == tech.Id);
                    if (allTech != null)
                        allTech.TruckNumber = truck.Truck.TruckNumber;
                }
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_busyLoading)
                return;

            _busyLoading = true;

            try
            {
                await LoadBoardAsync();
            }
            finally
            {
                _busyLoading = false;
            }
        }

        private async void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Clear all truck assignments for today?",
                "Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                SetStatus("Clearing board...");
                var d = DateTime.Today.ToString("yyyy-MM-dd");

                var resp = await _http.PostAsync($"api/trucks/board/clear?date={d}", content: null);
                resp.EnsureSuccessStatusCode();

                await LoadBoardAsync();
                SetStatus("Board cleared.");
            }
            catch (Exception ex)
            {
                SetStatus("Clear all failed: " + ex.Message);
                MessageBox.Show(ex.Message, "Clear All Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SetHome_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Set all active technicians to their home trucks for today?\n\nThis will replace the current board.",
                "Set Home",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                SetStatus("Setting home trucks...");
                var d = DateTime.Today.ToString("yyyy-MM-dd");

                var resp = await _http.PostAsync($"api/trucks/board/set-home?date={d}", content: null);
                resp.EnsureSuccessStatusCode();

                await LoadBoardAsync();
                SetStatus("Home trucks applied.");
            }
            catch (Exception ex)
            {
                SetStatus("Set home failed: " + ex.Message);
                MessageBox.Show(ex.Message, "Set Home Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TechnicianSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _technicianSearchText = TechnicianSearchBox.Text?.Trim() ?? "";
            RefreshTechnicianFilter();
        }

        private void ShowAllTechnicians_Changed(object sender, RoutedEventArgs e)
        {
            _showAllTechnicians = ShowAllTechniciansCheckBox.IsChecked == true;
            RefreshTechnicianFilter();
        }

        private void RefreshTechnicianFilter()
        {
            if (TechniciansList == null)
                return;

            var view = CollectionViewSource.GetDefaultView(Board.AllTechnicians);
            if (view == null)
                return;

            view.Filter = TechnicianDrawerFilter;
            view.Refresh();

            OnPropertyChanged(nameof(VisibleTechnicianCount));
            OnPropertyChanged(nameof(SelectedDrawerTechnicianCount));
            OnPropertyChanged(nameof(HasSelectedDrawerTechnicians));
        }

        private bool TechnicianDrawerFilter(object obj)
        {
            if (obj is not TechnicianDto tech)
                return false;

            if (!_showAllTechnicians && !string.IsNullOrWhiteSpace(tech.TruckNumber))
                return false;

            var q = (_technicianSearchText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(q))
                return true;

            bool Match(string? value) =>
                !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            return Match(tech.Name)
                || Match(tech.Title)
                || Match(tech.ScheduleText)
                || Match(tech.TruckNumber);
        }

        private void RemoveTechFromTruck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TechnicianDto tech)
                return;

            MoveTechnicianToTruck(tech.Id, null);
        }

        private void ClearTruck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TruckColumnVm truckVm)
                return;

            if (truckVm.Technicians.Count == 0)
                return;

            var confirm = MessageBox.Show(
                $"Remove all technicians from Truck {truckVm.Truck.TruckNumber}?",
                "Clear Truck",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            foreach (var tech in truckVm.Technicians.ToList())
                MoveTechnicianToTruck(tech.Id, null);
        }

        private void MoveTechnicianToTruck(int technicianId, int? toTruckId)
        {
            ApplyMoveLocally(technicianId, toTruckId);

            EnqueueMove(new MoveTechnicianRequest
            {
                WorkDate = DateTime.Today,
                TechnicianId = technicianId,
                ToTruckId = toTruckId
            });
        }

        private void ApplyMoveLocally(int techId, int? toTruckId)
        {
            var tech = Board.RemoveTechEverywhere(techId)
                ?? Board.AllTechnicians.FirstOrDefault(x => x.Id == techId);

            if (tech == null)
                return;

            if (toTruckId == null)
            {
                Board.InsertSorted(Board.Unassigned, tech);
            }
            else
            {
                var truck = Board.Trucks.FirstOrDefault(t => t.Truck.Id == toTruckId.Value);

                if (truck == null)
                    Board.InsertSorted(Board.Unassigned, tech);
                else
                    Board.InsertSorted(truck.Technicians, tech);
            }

            NormalizeTruckAssignments();
            RefreshBoardMetrics();
            RefreshTechnicianFilter();
        }

        private void EnqueueMove(MoveTechnicianRequest req)
        {
            lock (_queueLock)
            {
                _latestMoveByTech[req.TechnicianId] = req;

                if (_pendingTechs.Add(req.TechnicianId))
                    _techQueue.Enqueue(req.TechnicianId);

                _moveRunner ??= Task.Run(ProcessMoveQueueAsync);
            }
        }

        private async Task ProcessMoveQueueAsync()
        {
            while (true)
            {
                int techId;
                MoveTechnicianRequest req;

                lock (_queueLock)
                {
                    if (_techQueue.Count == 0)
                    {
                        _moveRunner = null;
                        return;
                    }

                    techId = _techQueue.Dequeue();
                    _pendingTechs.Remove(techId);

                    if (!_latestMoveByTech.TryGetValue(techId, out req!))
                        continue;

                    _latestMoveByTech.Remove(techId);
                }

                try
                {
                    await Dispatcher.InvokeAsync(() => SetStatus("Saving changes..."));

                    var resp = await _http.PutAsJsonAsync("api/trucks/board/move", req);
                    resp.EnsureSuccessStatusCode();
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() => SetStatus("Save error — reloading: " + ex.Message));
                    await Dispatcher.InvokeAsync(async () => await LoadBoardAsync());
                }
                finally
                {
                    await Dispatcher.InvokeAsync(() => SetStatus("Ready."));
                }
            }
        }

        private void TechniciansList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTechnicianSelection)
                return;

            _selectedTechnicianIds.Clear();

            if (TechniciansList?.SelectedItems != null)
            {
                foreach (var tech in TechniciansList.SelectedItems.OfType<TechnicianDto>())
                    _selectedTechnicianIds.Add(tech.Id);
            }

            OnPropertyChanged(nameof(SelectedDrawerTechnicianCount));
            OnPropertyChanged(nameof(HasSelectedDrawerTechnicians));
        }

        private List<TechnicianDto> GetSelectedDrawerTechnicians()
        {
            if (_selectedTechnicianIds.Count == 0)
                return new List<TechnicianDto>();

            return Board.AllTechnicians
                .Where(t => _selectedTechnicianIds.Contains(t.Id))
                .GroupBy(t => t.Id)
                .Select(g => g.First())
                .ToList();
        }

        private void AddSelectedToTruck_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TruckColumnVm truckVm)
                return;

            var selectedTechs = GetSelectedDrawerTechnicians();

            if (selectedTechs.Count == 0)
            {
                MessageBox.Show(
                    "Select one or more technicians from the right side first.",
                    "Add Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var targetTruckId = truckVm.Truck.Id;

            var movedCount = 0;

            foreach (var tech in selectedTechs)
            {
                var alreadyOnThisTruck = truckVm.Technicians.Any(x => x.Id == tech.Id);

                if (alreadyOnThisTruck)
                    continue;

                MoveTechnicianToTruck(tech.Id, targetTruckId);
                movedCount++;
            }

            ClearTechnicianDrawerSelection();

            if (movedCount == 0)
            {
                SetStatus($"Selected technician(s) are already on Truck {truckVm.Truck.TruckNumber}.");
                return;
            }

            SetStatus($"Moved {movedCount} technician(s) to Truck {truckVm.Truck.TruckNumber}.");
        }

        private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;

            // Do not clear when clicking inside the technician drawer list.
            if (IsDescendantOf(source, TechniciansList))
                return;

            // Do not clear before buttons run, especially Add Selected.
            if (FindAncestor<Button>(source) != null)
                return;

            // Do not clear while using the search box.
            if (FindAncestor<TextBox>(source) != null)
                return;

            ClearTechnicianDrawerSelection();
        }

        private void ClearTechnicianDrawerSelection()
        {
            _selectedTechnicianIds.Clear();

            _syncingTechnicianSelection = true;

            try
            {
                ClearAllListBoxSelections(this);
            }
            finally
            {
                _syncingTechnicianSelection = false;
            }

            OnPropertyChanged(nameof(SelectedDrawerTechnicianCount));
            OnPropertyChanged(nameof(HasSelectedDrawerTechnicians));
        }

        private static bool IsDescendantOf(DependencyObject? child, DependencyObject? parent)
        {
            if (child == null || parent == null)
                return false;

            var current = child;

            while (current != null)
            {
                if (ReferenceEquals(current, parent))
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static T? FindAncestor<T>(DependencyObject? current)
            where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void AssignedTruckTechList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tech = e.AddedItems
                .OfType<TechnicianDto>()
                .FirstOrDefault();

            if (tech == null)
                return;

            SelectTechnicianForMove(tech);
        }

        private void SelectTechnicianForMove(TechnicianDto tech)
        {
            _selectedTechnicianIds.Clear();
            _selectedTechnicianIds.Add(tech.Id);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncDrawerSelectionToSelectedIds();

                OnPropertyChanged(nameof(SelectedDrawerTechnicianCount));
                OnPropertyChanged(nameof(HasSelectedDrawerTechnicians));

                SetStatus($"Selected {tech.Name}. Click Add Selected on the target truck to move them.");
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SyncDrawerSelectionToSelectedIds()
        {
            if (TechniciansList == null)
                return;

            _syncingTechnicianSelection = true;

            try
            {
                TechniciansList.SelectedItems.Clear();

                foreach (var tech in Board.AllTechnicians.Where(t => _selectedTechnicianIds.Contains(t.Id)))
                {
                    // Only select in the right drawer if the item is currently visible under the filter.
                    if (TechnicianDrawerFilter(tech))
                    {
                        TechniciansList.SelectedItems.Add(tech);
                        TechniciansList.ScrollIntoView(tech);
                    }
                }
            }
            finally
            {
                _syncingTechnicianSelection = false;
            }
        }

        private static void ClearAllListBoxSelections(DependencyObject root)
        {
            if (root is ListBox listBox)
            {
                if (listBox.SelectionMode == SelectionMode.Single)
                {
                    listBox.SelectedIndex = -1;
                }
                else if (listBox.SelectedItems.Count > 0)
                {
                    listBox.SelectedItems.Clear();
                }
            }

            var count = VisualTreeHelper.GetChildrenCount(root);

            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                ClearAllListBoxSelections(child);
            }
        }
    }

    public sealed class TruckBoardVm
    {
        public DateTime WorkDate { get; set; } = DateTime.Today;
        public ObservableCollection<TechnicianDto> Unassigned { get; set; } = new();
        public ObservableCollection<TechnicianDto> AllTechnicians { get; set; } = new();
        public ObservableCollection<TruckColumnVm> Trucks { get; set; } = new();

        public static TruckBoardVm FromDto(TruckBoardDto? dto)
        {
            var vm = new TruckBoardVm();
            if (dto == null)
                return vm;

            vm.WorkDate = dto.WorkDate.Date;

            vm.Unassigned = new ObservableCollection<TechnicianDto>(
                (dto.Unassigned ?? new List<TechnicianDto>())
                    .OrderByDescending(t => t.IsOnShift)
                    .ThenBy(t => t.Name));

            vm.AllTechnicians = new ObservableCollection<TechnicianDto>(
                (dto.AllTechnicians ?? new List<TechnicianDto>())
                    .OrderByDescending(t => t.IsOnShift)
                    .ThenBy(t => t.Name));

            vm.Trucks = new ObservableCollection<TruckColumnVm>(
                (dto.Trucks ?? new List<TruckColumnDto>())
                .Select(c => new TruckColumnVm
                {
                    Truck = c.Truck,
                    Technicians = new ObservableCollection<TechnicianDto>(
                        (c.Technicians ?? new List<TechnicianDto>())
                            .OrderBy(t => t.Name))
                }));

            return vm;
        }

        public TechnicianDto? RemoveTechEverywhere(int techId)
        {
            var u = Unassigned.FirstOrDefault(x => x.Id == techId);
            if (u != null)
                Unassigned.Remove(u);

            TechnicianDto? found = u;

            foreach (var t in Trucks)
            {
                var assigned = t.Technicians.FirstOrDefault(x => x.Id == techId);
                if (assigned != null)
                {
                    t.Technicians.Remove(assigned);
                    found ??= assigned;
                }
            }

            return found;
        }

        public void InsertSorted(ObservableCollection<TechnicianDto> list, TechnicianDto tech)
        {
            if (list.Any(x => x.Id == tech.Id))
                return;

            var ordered = list
                .Concat(new[] { tech })
                .OrderByDescending(x => x.IsOnShift)
                .ThenBy(x => x.Name)
                .ToList();

            list.Clear();

            foreach (var item in ordered)
                list.Add(item);
        }
    }

    public sealed class TruckColumnVm
    {
        public TruckDto Truck { get; set; } = new();
        public ObservableCollection<TechnicianDto> Technicians { get; set; } = new();
    }
}