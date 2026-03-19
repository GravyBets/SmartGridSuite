#nullable enable
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TechniciansPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private IEnumerable<TechnicianDto> AllTechnicians =>
    Board.Unassigned
         .Concat(Board.Trucks.SelectMany(t => t.Technicians))
         .GroupBy(t => t.Id)
         .Select(g => g.First());

        public int OnDutyCount => AllTechnicians.Count(t => t.IsOnShift);

        public int OffDutyCount => AllTechnicians.Count(t => !t.IsOnShift);

        public int AssignedCount => Board.Trucks.Sum(t => t.Technicians.Count);

        public int UnassignedCount => Board.Unassigned.Count;

        private void RefreshBoardMetrics()
        {
            OnPropertyChanged(nameof(Board));
            OnPropertyChanged(nameof(OnDutyCount));
            OnPropertyChanged(nameof(OffDutyCount));
            OnPropertyChanged(nameof(AssignedCount));
            OnPropertyChanged(nameof(UnassignedCount));
        }

        private readonly HttpClient _http;

        private bool _busyLoading;

        public TruckBoardVm Board { get; private set; } = new();

        private bool _unassignedVisible = true;

        private Point _dragStart;
        private bool _mouseDown;

        // Coalesced move queue (only last move per tech is sent)
        private readonly object _queueLock = new();
        private readonly Queue<int> _techQueue = new();
        private readonly HashSet<int> _pendingTechs = new();
        private readonly Dictionary<int, MoveTechnicianRequest> _latestMoveByTech = new();
        private Task? _moveRunner;

        public TechniciansPaneView()
        {
            InitializeComponent();
            DataContext = this;

            _http = new HttpClient { BaseAddress = new Uri("https://localhost:7140/") };

            Loaded += async (_, __) => await InitializeAndLoadAsync();
        }

        private void SetStatus(string msg) => StatusText.Text = msg;

        // ---- Load / refresh ----

        private async Task InitializeAndLoadAsync()
        {
            if (_busyLoading) return;

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
                RefreshBoardMetrics();

                SetStatus($"Loaded {Board.Unassigned.Count} available, {Board.Trucks.Count} trucks.");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_busyLoading) return;
            _busyLoading = true;
            try { await LoadBoardAsync(); }
            finally { _busyLoading = false; }
        }

        // ---- Collapse / expand Unassigned ----

        private void ToggleUnassigned_Click(object sender, RoutedEventArgs e)
        {
            _unassignedVisible = !_unassignedVisible;

            if (_unassignedVisible)
            {
                UnassignedColumn.Width = new GridLength(320);
                UnassignedCard.Visibility = Visibility.Visible;
                ToggleUnassignedBtn.Content = "Hide Available";
            }
            else
            {
                UnassignedColumn.Width = new GridLength(0);
                UnassignedCard.Visibility = Visibility.Collapsed;
                ToggleUnassignedBtn.Content = "Show Available";
            }
        }

        // ---- Drag / Drop ----

        private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = true;
            _dragStart = e.GetPosition(null);
        }

        private void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_mouseDown) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(null);
            var dx = Math.Abs(pos.X - _dragStart.X);
            var dy = Math.Abs(pos.Y - _dragStart.Y);

            if (dx < SystemParameters.MinimumHorizontalDragDistance &&
                dy < SystemParameters.MinimumVerticalDragDistance)
                return;

            _mouseDown = false;

            if (sender is not ListBox listBox) return;
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item?.DataContext is not TechnicianDto tech) return;

            DragDrop.DoDragDrop(listBox, tech, DragDropEffects.Move);
        }

        private void List_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TechnicianDto))) return;
            var tech = (TechnicianDto)e.Data.GetData(typeof(TechnicianDto))!;

            if (sender is not ListBox targetList) return;

            int? toTruckId = null;
            if (targetList.Tag is int tid)
                toTruckId = tid;
            else if (targetList.Tag != null && int.TryParse(targetList.Tag.ToString(), out var parsed))
                toTruckId = parsed;

            // Optimistic UI update first (instant)
            ApplyMoveLocally(tech.Id, toTruckId);

            // Queue the server update (coalesced + serialized)
            EnqueueMove(new MoveTechnicianRequest
            {
                WorkDate = DateTime.Today,
                TechnicianId = tech.Id,
                ToTruckId = toTruckId
            });
        }

        private void ApplyMoveLocally(int techId, int? toTruckId)
        {
            var tech = Board.RemoveTechEverywhere(techId);
            if (tech == null) return;

            if (toTruckId == null)
                Board.InsertSorted(Board.Unassigned, tech);
            else
            {
                var truck = Board.Trucks.FirstOrDefault(t => t.Truck.Id == toTruckId.Value);
                if (truck == null)
                {
                    // destination truck doesn't exist in UI; fall back to unassigned
                    Board.InsertSorted(Board.Unassigned, tech);
                }
                else
                {
                    Board.InsertSorted(truck.Technicians, tech);
                }
            }
            RefreshBoardMetrics();
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

                    // If for some reason missing, skip
                    if (!_latestMoveByTech.TryGetValue(techId, out req!))
                        continue;

                    // Consume the latest move for this tech
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
                    // If saving fails, reload to get back to truth
                    await Dispatcher.InvokeAsync(() => SetStatus("Save error — reloading: " + ex.Message));
                    await Dispatcher.InvokeAsync(async () => await LoadBoardAsync());
                }
                finally
                {
                    await Dispatcher.InvokeAsync(() => SetStatus("Ready."));
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }

    // ---- Tiny ViewModel layer to make UI updates instant ----

    public sealed class TruckBoardVm
    {
        public DateTime WorkDate { get; set; } = DateTime.Today;
        public ObservableCollection<TechnicianDto> Unassigned { get; set; } = new();
        public ObservableCollection<TruckColumnVm> Trucks { get; set; } = new();

        public static TruckBoardVm FromDto(TruckBoardDto? dto)
        {
            var vm = new TruckBoardVm();
            if (dto == null) return vm;

            vm.WorkDate = dto.WorkDate.Date;

            vm.Unassigned = new ObservableCollection<TechnicianDto>(
                (dto.Unassigned ?? new List<TechnicianDto>()).OrderByDescending(t => t.IsOnShift).ThenBy(t => t.Name));

            vm.Trucks = new ObservableCollection<TruckColumnVm>(
                (dto.Trucks ?? new List<TruckColumnDto>()).Select(c => new TruckColumnVm
                {
                    Truck = c.Truck,
                    Technicians = new ObservableCollection<TechnicianDto>((c.Technicians ?? new List<TechnicianDto>()).OrderBy(t => t.Name))
                }));

            return vm;
        }

        public TechnicianDto? RemoveTechEverywhere(int techId)
        {
            // Unassigned
            var u = Unassigned.FirstOrDefault(x => x.Id == techId);
            if (u != null)
            {
                Unassigned.Remove(u);
                return u;
            }

            // Any truck
            foreach (var t in Trucks)
            {
                var found = t.Technicians.FirstOrDefault(x => x.Id == techId);
                if (found != null)
                {
                    t.Technicians.Remove(found);
                    return found;
                }
            }

            return null;
        }

        public void InsertSorted(ObservableCollection<TechnicianDto> list, TechnicianDto tech)
        {
            // Keep it readable: sort by Name (and you can change this later)
            var idx = 0;
            while (idx < list.Count && string.Compare(list[idx].Name, tech.Name, StringComparison.OrdinalIgnoreCase) < 0)
                idx++;

            list.Insert(idx, tech);
        }
    }

    public sealed class TruckColumnVm
    {
        public TruckDto Truck { get; set; } = new();
        public ObservableCollection<TechnicianDto> Technicians { get; set; } = new();
    }
}