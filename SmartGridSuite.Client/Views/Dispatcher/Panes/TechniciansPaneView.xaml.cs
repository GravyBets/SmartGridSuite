#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.Technicians;
using SmartGridSuite.Contracts.Administration.Trucks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TechniciansPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly HttpClient _http;

        private bool _hasUnsavedChanges;

        private Point _dragStartPoint;
        private const string TechnicianDragFormat = "SmartGridSuite.TechnicianDrag";
        private const string TechnicianDragIdsFormat = "SmartGridSuite.TechnicianDragIds";
        private List<int> _dragTechnicianIdsSnapshot = new();

        private bool _busyLoading;
        private bool _showAllTechnicians;
        private string _technicianSearchText = "";
        private bool _isCommitting;

        private readonly HashSet<int> _selectedTechnicianIds = new();

        private bool _syncingTechnicianSelection;

        private int _busyOverlayDepth;

        private DateTime _selectedWorkDate = DateTime.Today;
        private bool _syncingWorkDate;

        public DateTime SelectedWorkDate
        {
            get => _selectedWorkDate;

            set
            {
                var normalizedDate =
                    (value == default
                        ? DateTime.Today
                        : value).Date;

                if (_selectedWorkDate == normalizedDate)
                    return;

                _selectedWorkDate = normalizedDate;

                OnPropertyChanged();
                OnPropertyChanged(nameof(BoardSubtitle));
            }
        }

        public string BoardSubtitle => $"Build crews for {SelectedWorkDate:dddd, MMMM d, yyyy}.";

        public TruckBoardVm Board { get; private set; } = new();

        public int OnDutyCount => Board.AllTechnicians.Count(t => t.IsOnShift);
        public int OffDutyCount => Board.AllTechnicians.Count(t => !t.IsOnShift);
        public int AssignedCount => Board.Trucks.Sum(t => t.Technicians.Count);
        public int UnassignedCount => Board.AllTechnicians.Count(t => string.IsNullOrWhiteSpace(t.TruckNumber));
        public int TrucksUsedCount => Board.Trucks.Count(t => t.Technicians.Count > 0);
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (_hasUnsavedChanges == value)
                    return;

                _hasUnsavedChanges = value;
                NotifyDraftStateChanged();
            }
        }
        public string DraftStatusText =>
            HasUnsavedChanges
                ? "Unsaved truck board changes"
                : "Truck board saved";

        //Committing Changes
        public bool IsCommitting
        {
            get => _isCommitting;
            private set
            {
                if (_isCommitting == value)
                    return;

                _isCommitting = value;
                NotifyDraftStateChanged();
            }
        }
        public bool CanEditBoard => !IsCommitting;
        public bool CanCommitChanges => HasUnsavedChanges && !IsCommitting;
        public bool CanDiscardChanges => HasUnsavedChanges && !IsCommitting;
        public string CommitButtonText => IsCommitting ? "Committing..." : "Save";

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

            _http = ClientAppSettings.CreateHttpClient();

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
                SetStatus("Loading board...");

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

        private async Task LoadBoardAsync(string busyMessage = "Loading truck board...")
        {
            ShowBusyOverlay(busyMessage);

            try
            {
                SetStatus("Loading board...");

                var d = SelectedWorkDate.ToString("yyyy-MM-dd");

                var dto =
                    await _http.GetFromJsonAsync<TruckBoardDto>(
                        $"api/trucks/board?date={d}");

                Board = TruckBoardVm.FromDto(dto);
                if (dto != null && dto.WorkDate != default)
                {
                    SelectedWorkDate =
                        dto.WorkDate.Date;
                }
                NormalizeTruckAssignments();
                RecalculateLocalLeads();
                HasUnsavedChanges = false;
                RefreshBoardMetrics();
                RefreshTechnicianFilter();

                SetStatus($"Loaded {Board.AllTechnicians.Count} technicians, {Board.Trucks.Count} trucks.");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                HideBusyOverlay();
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
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(DraftStatusText));
            OnPropertyChanged(nameof(IsCommitting));
            OnPropertyChanged(nameof(CanEditBoard));
            OnPropertyChanged(nameof(CanCommitChanges));
            OnPropertyChanged(nameof(CanDiscardChanges));
            OnPropertyChanged(nameof(CommitButtonText));
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
            if (IsCommitting)
                return;

            if (_busyLoading)
                return;

            if (HasUnsavedChanges)
            {
                var confirm = MessageBox.Show(
                    "Refresh will discard your uncommitted truck board changes. Continue?",
                    "Discard Changes?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            _busyLoading = true;

            try
            {
                await LoadBoardAsync("Refreshing truck board...");
            }
            finally
            {
                _busyLoading = false;
            }
        }

        private async void PreviousDay_Click(object sender, RoutedEventArgs e)
        {
            await ChangeWorkDateAsync(
                SelectedWorkDate.AddDays(-1));
        }

        private async void Today_Click(object sender, RoutedEventArgs e)
        {
            await ChangeWorkDateAsync(
                DateTime.Today);
        }

        private async void NextDay_Click(object sender, RoutedEventArgs e)
        {
            await ChangeWorkDateAsync(
                SelectedWorkDate.AddDays(1));
        }

        private async void WorkDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingWorkDate ||
                !IsLoaded ||
                WorkDatePicker.SelectedDate is not DateTime selectedDate)
            {
                return;
            }

            await ChangeWorkDateAsync(
                selectedDate);
        }

        private async Task ChangeWorkDateAsync(DateTime requestedDate)
        {
            var newDate =
                requestedDate.Date;

            if (newDate == SelectedWorkDate)
            {
                RestoreWorkDatePicker();
                return;
            }

            if (_busyLoading ||
                IsCommitting)
            {
                RestoreWorkDatePicker();
                return;
            }

            if (HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    $"You have unsaved truck board changes for " +
                    $"{SelectedWorkDate:dddd, MMMM d, yyyy}.\n\n" +
                    $"Do you want to save them before switching dates?\n\n" +
                    "Yes = Save and switch dates\n" +
                    "No = Discard and switch dates\n" +
                    "Cancel = Stay on the current date",
                    "Unsaved Truck Board Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    RestoreWorkDatePicker();
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    var saved =
                        await CommitBoardChangesAsync(
                            showConfirmation: false);

                    if (!saved)
                    {
                        RestoreWorkDatePicker();
                        return;
                    }
                }
                else
                {
                    HasUnsavedChanges = false;
                }
            }

            _syncingWorkDate = true;

            try
            {
                SelectedWorkDate = newDate;
                WorkDatePicker.SelectedDate = newDate;
            }
            finally
            {
                _syncingWorkDate = false;
            }

            _busyLoading = true;

            try
            {
                await LoadBoardAsync(
                    $"Loading truck board for " +
                    $"{SelectedWorkDate:dddd, MMMM d, yyyy}...");
            }
            finally
            {
                _busyLoading = false;
            }
        }

        private void RestoreWorkDatePicker()
        {
            _syncingWorkDate = true;

            try
            {
                WorkDatePicker.SelectedDate =
                    SelectedWorkDate;
            }
            finally
            {
                _syncingWorkDate = false;
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (IsCommitting)
                return;

            var confirm = MessageBox.Show(
                "Clear all truck assignments locally?\n\nNothing will be saved until you click Commit Changes.",
                "Clear All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            ClearBoardLocally();

            HasUnsavedChanges = true;
            SetStatus("Board cleared locally. Click Commit Changes to save.");
        }

        private void SetHome_Click(object sender, RoutedEventArgs e)
        {
            if (IsCommitting)
                return;

            var confirm = MessageBox.Show(
                "Set all active technicians to their home trucks locally?\n\nThis will replace the current draft board. Nothing will be saved until you click Commit Changes.",
                "Set Home",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            ClearBoardLocally(markChanged: false);

            var activeTruckIds = Board.Trucks
                .Select(t => t.Truck.Id)
                .ToHashSet();

            foreach (var tech in Board.AllTechnicians.ToList())
            {
                if (tech.HomeTruckId.HasValue &&
                    activeTruckIds.Contains(tech.HomeTruckId.Value))
                {
                    ApplyMoveLocally(tech.Id, tech.HomeTruckId.Value, refreshAfterMove: false);
                }
            }

            NormalizeTruckAssignments();
            RecalculateLocalLeads();
            RefreshBoardMetrics();
            RefreshTechnicianFilter();

            MarkBoardDirty("Home trucks applied locally. Click Commit Changes to save.");
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
            if (IsCommitting)
                return;

            if (sender is not Button button || button.Tag is not TechnicianDto tech)
                return;

            MoveTechnicianToTruck(tech.Id, null);
        }

        private void SetLeadTechnician_Click(object sender, RoutedEventArgs e)
        {
            if (IsCommitting)
                return;

            if (sender is not Button button || button.Tag is not TechnicianDto tech)
                return;

            var truckVm = Board.Trucks.FirstOrDefault(t =>
                t.Technicians.Any(x => x.Id == tech.Id));

            if (truckVm == null)
                return;

            if (truckVm.Technicians.Count < 2)
            {
                MessageBox.Show(
                    "A lead can only be set when two or more technicians are assigned to the truck.",
                    "Set Lead",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            truckVm.LeadTechnicianId = tech.Id;
            MarkBoardDirty();

            SetStatus($"{tech.Name} set as local lead for Truck {truckVm.Truck.TruckNumber}. Click Commit Changes to save.");
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

        private async void CommitChanges_Click(object sender, RoutedEventArgs e)
        {
            await CommitBoardChangesAsync(showConfirmation: true);
        }

        private async Task<bool> CommitBoardChangesAsync(bool showConfirmation)
        {
            if (!HasUnsavedChanges || IsCommitting)
                return true;

            var assignments = Board.Trucks
                .SelectMany(truck => truck.Technicians.Select(tech => new CommitTruckAssignmentDto
                {
                    TechnicianId = tech.Id,
                    TruckId = truck.Truck.Id
                }))
                .ToList();

            var leadOverrides = Board.Trucks
                .Where(truck =>
                    truck.Technicians.Count >= 2 &&
                    truck.LeadTechnicianId.HasValue &&
                    truck.Technicians.Any(tech => tech.Id == truck.LeadTechnicianId.Value))
                .Select(truck => new CommitTruckLeadOverrideDto
                {
                    TruckId = truck.Truck.Id,
                    TechnicianId = truck.LeadTechnicianId!.Value
                })
                .ToList();

            if (showConfirmation)
            {
                var confirm = MessageBox.Show(
                    $"Commit the truck board for " +
                    $"{SelectedWorkDate:dddd, MMMM d, yyyy}?\n\n" +
                    $"Assigned technicians: {assignments.Count}\n" +
                    $"Lead overrides: {leadOverrides.Count}\n\n" +
                    "This will save the current truck/crew layout to the server.",
                    "Commit Truck Board",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                    return false;
            }

            try
            {
                IsCommitting = true;
                ShowBusyOverlay("Committing truck board changes...");
                SetStatus("Committing truck board changes...");

                var req = new CommitTruckBoardRequest
                {
                    WorkDate = SelectedWorkDate,
                    Assignments = assignments,
                    LeadOverrides = leadOverrides
                };

                var resp = await _http.PutAsJsonAsync("api/trucks/board/commit", req);

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();

                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(body)
                            ? $"Commit failed: {(int)resp.StatusCode} {resp.ReasonPhrase}"
                            : body,
                        "Commit Changes Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return false;
                }

                await LoadBoardAsync("Refreshing saved truck board...");

                SetStatus("Truck board changes committed.");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Commit failed: " + ex.Message);

                MessageBox.Show(
                    ex.ToString(),
                    "Commit Changes Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }
            finally
            {
                IsCommitting = false;
                HideBusyOverlay();
            }
        }

        public async Task<bool> ConfirmLeaveIfDirtyAsync()
        {
            if (!HasUnsavedChanges)
                return true;

            if (IsCommitting)
                return false;

            var result = MessageBox.Show(
                "You have unsaved truck board changes.\n\n" +
                "Do you want to commit these changes before leaving?\n\n" +
                "Yes = Commit changes and continue\n" +
                "No = Discard changes and continue\n" +
                "Cancel = Stay on Truck Assignments",
                "Unsaved Truck Board Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.No)
            {
                HasUnsavedChanges = false;
                return true;
            }

            return await CommitBoardChangesAsync(showConfirmation: false);
        }

        private void NotifyDraftStateChanged()
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(DraftStatusText));
            OnPropertyChanged(nameof(IsCommitting));
            OnPropertyChanged(nameof(CanEditBoard));
            OnPropertyChanged(nameof(CanCommitChanges));
            OnPropertyChanged(nameof(CanDiscardChanges));
            OnPropertyChanged(nameof(CommitButtonText));
        }

        private void MarkBoardDirty(string? status = null)
        {
            HasUnsavedChanges = true;
            NotifyDraftStateChanged();

            if (!string.IsNullOrWhiteSpace(status))
                SetStatus(status);
        }

        private async void DiscardChanges_Click(object sender, RoutedEventArgs e)
        {
            if (IsCommitting)
                return;

            if (!HasUnsavedChanges)
                return;

            var confirm = MessageBox.Show(
                "Discard all uncommitted truck board changes and reload from the server?",
                "Discard Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            await LoadBoardAsync("Discarding changes and reloading truck board...");
        }

        private void MoveTechnicianToTruck(int technicianId, int? toTruckId)
        {
            if (IsCommitting)
                return;

            ApplyMoveLocally(technicianId, toTruckId);

            MarkBoardDirty("Truck board changed locally. Click Commit Changes to save.");
        }

        private void ApplyMoveLocally(int techId, int? toTruckId, bool refreshAfterMove = true)
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

            if (!refreshAfterMove)
                return;

            NormalizeTruckAssignments();
            RecalculateLocalLeads();
            RefreshBoardMetrics();
            RefreshTechnicianFilter();
        }

        private void ClearBoardLocally(bool markChanged = true)
        {
            foreach (var truck in Board.Trucks)
            {
                truck.Technicians.Clear();
                truck.LeadTechnicianId = null;
            }

            Board.Unassigned.Clear();

            foreach (var tech in Board.AllTechnicians)
            {
                tech.TruckNumber = null;
                Board.InsertSorted(Board.Unassigned, tech);
            }

            NormalizeTruckAssignments();
            RefreshBoardMetrics();
            RefreshTechnicianFilter();

            if (markChanged)
                MarkBoardDirty();
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
            if (IsCommitting)
                return;

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
            if (BusyOverlay?.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                return;
            }

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

                current = GetParentObject(current);
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

                current = GetParentObject(current);
            }

            return null;
        }

        private static DependencyObject? GetParentObject(DependencyObject? child)
        {
            if (child == null)
                return null;

            // Normal visual tree path.
            if (child is Visual || child is Visual3D)
                return VisualTreeHelper.GetParent(child);

            // Handles TextBlock child content like Run, Span, Bold, etc.
            if (child is ContentElement contentElement)
            {
                var parent = ContentOperations.GetParent(contentElement);

                if (parent != null)
                    return parent;

                if (contentElement is FrameworkContentElement frameworkContentElement)
                    return frameworkContentElement.Parent;
            }

            // Fallback for logical tree-only objects.
            return LogicalTreeHelper.GetParent(child);
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

        private void RecalculateLocalLeads()
        {
            foreach (var truck in Board.Trucks)
            {
                if (truck.Technicians.Count <= 1)
                {
                    truck.LeadTechnicianId = null;
                    continue;
                }

                if (truck.LeadTechnicianId.HasValue &&
                    truck.Technicians.Any(t => t.Id == truck.LeadTechnicianId.Value))
                {
                    continue;
                }

                var lead = PickLocalLeadTechnician(truck);

                truck.LeadTechnicianId = lead?.Id;
            }
        }

        private static TechnicianDto? PickLocalLeadTechnician(TruckColumnVm truck)
        {
            var homeTruckLead = truck.Technicians
                .FirstOrDefault(t => t.HomeTruckId == truck.Truck.Id);

            if (homeTruckLead != null)
                return homeTruckLead;

            return truck.Technicians
                .OrderByDescending(GetTitleRank)
                .ThenBy(t => t.Name)
                .FirstOrDefault();
        }

        private static int GetTitleRank(TechnicianDto tech)
        {
            var title = (tech.Title ?? string.Empty).Trim();

            if (title.Equals("Supervisor", StringComparison.OrdinalIgnoreCase))
                return 400;

            if (title.Equals("Head Journeyman", StringComparison.OrdinalIgnoreCase))
                return 300;

            if (title.Equals("Journeyman", StringComparison.OrdinalIgnoreCase))
                return 200;

            if (title.Equals("Apprentice", StringComparison.OrdinalIgnoreCase))
                return 100;

            return 0;
        }

        private void TechCardList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _dragTechnicianIdsSnapshot.Clear();

            if (IsCommitting)
                return;

            var source = e.OriginalSource as DependencyObject;

            // Let buttons and checkboxes behave normally.
            if (FindAncestor<Button>(source) != null ||
                FindAncestor<CheckBox>(source) != null)
            {
                return;
            }

            var item = FindAncestor<ListBoxItem>(source);

            if (item?.DataContext is not TechnicianDto tech)
                return;

            var listBox = FindAncestor<ListBox>(item);

            // Drawer supports multi-select.
            if (ReferenceEquals(listBox, TechniciansList))
            {
                if (_selectedTechnicianIds.Contains(tech.Id))
                {
                    _dragTechnicianIdsSnapshot = _selectedTechnicianIds
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();

                    // Important: prevents WPF from toggling this selected card off
                    // on the click used to start dragging.
                    e.Handled = true;
                    return;
                }

                _dragTechnicianIdsSnapshot = new List<int> { tech.Id };
                return;
            }

            // Assigned truck lists are single-card drags.
            _dragTechnicianIdsSnapshot = new List<int> { tech.Id };

            if (item.IsSelected)
                e.Handled = true;
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
            SetTruckBoardControlsEnabled(false);

            Cursor = Cursors.Wait;
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
            SetTruckBoardControlsEnabled(true);

            Cursor = null;

            RefreshBoardMetrics();
            NotifyDraftStateChanged();
        }

        private void SetTruckBoardControlsEnabled(bool enabled)
        {
            if (RefreshBoardButton != null)
                RefreshBoardButton.IsEnabled = enabled && CanEditBoard;

            if (TechnicianDrawerCard != null)
                TechnicianDrawerCard.IsEnabled = enabled && CanEditBoard;

            if (TruckBoardCard != null)
                TruckBoardCard.IsEnabled = enabled && CanEditBoard;

            if (!enabled)
                _dragTechnicianIdsSnapshot.Clear();
        }


        //Drag and Drop
        private void TechCardList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (IsCommitting)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var currentPosition = e.GetPosition(null);

            if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
                return;

            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);

            if (item?.DataContext is not TechnicianDto tech)
                return;

            var draggedIds = _dragTechnicianIdsSnapshot.Count > 0
                ? _dragTechnicianIdsSnapshot
                : GetDraggedTechnicianIds(tech.Id);

            if (draggedIds.Count == 0)
                return;

            var data = new DataObject();
            data.SetData(TechnicianDragFormat, tech.Id);
            data.SetData(TechnicianDragIdsFormat, draggedIds.ToArray());

            DragDrop.DoDragDrop(item, data, DragDropEffects.Move);

            _dragTechnicianIdsSnapshot.Clear();
            e.Handled = true;
        }

        private void TruckCard_DragOver(object sender, DragEventArgs e)
        {
            if (IsCommitting || !e.Data.GetDataPresent(TechnicianDragFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void TruckCard_Drop(object sender, DragEventArgs e)
        {
            if (IsCommitting)
                return;

            if (sender is not Border border || border.Tag is not TruckColumnVm truckVm)
                return;

            var techIdsToMove = GetDraggedTechnicianIdsFromDrop(e);

            if (techIdsToMove.Count == 0)
                return;

            foreach (var techId in techIdsToMove)
            {
                if (truckVm.Technicians.Any(t => t.Id == techId))
                    continue;

                MoveTechnicianToTruck(techId, truckVm.Truck.Id);
            }

            ClearTechnicianDrawerSelection();

            e.Handled = true;
        }

        private void TechnicianDrawer_DragOver(object sender, DragEventArgs e)
        {
            if (IsCommitting || !e.Data.GetDataPresent(TechnicianDragFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void TechnicianDrawer_Drop(object sender, DragEventArgs e)
        {
            if (IsCommitting)
                return;

            var techIdsToMove = GetDraggedTechnicianIdsFromDrop(e);

            if (techIdsToMove.Count == 0)
                return;

            foreach (var techId in techIdsToMove)
                MoveTechnicianToTruck(techId, null);

            ClearTechnicianDrawerSelection();

            e.Handled = true;
        }

        private bool TryGetDraggedTechnicianId(DragEventArgs e, out int technicianId)
        {
            technicianId = 0;

            if (!e.Data.GetDataPresent(TechnicianDragFormat))
                return false;

            var value = e.Data.GetData(TechnicianDragFormat);

            if (value is int id)
            {
                technicianId = id;
                return technicianId > 0;
            }

            return int.TryParse(value?.ToString(), out technicianId) && technicianId > 0;
        }

        private List<int> GetDraggedTechnicianIds(int draggedTechId)
        {
            // If the dragged tech is part of the selected drawer group, move the whole group.
            if (_selectedTechnicianIds.Contains(draggedTechId))
            {
                return _selectedTechnicianIds
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
            }

            // Otherwise, only move the card being dragged.
            return new List<int> { draggedTechId };
        }

        private List<int> GetDraggedTechnicianIdsFromDrop(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(TechnicianDragIdsFormat))
            {
                var value = e.Data.GetData(TechnicianDragIdsFormat);

                if (value is int[] ids)
                {
                    return ids
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();
                }
            }

            return TryGetDraggedTechnicianId(e, out var singleId)
                ? new List<int> { singleId }
                : new List<int>();
        }

        private void TruckBoardScrollViewer_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            scrollViewer.ScrollToVerticalOffset(
                scrollViewer.VerticalOffset - e.Delta);

            e.Handled = true;
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
                LeadTechnicianId = c.LeadTechnicianId,
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

    public sealed class TruckColumnVm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public TruckDto Truck { get; set; } = new();
        public ObservableCollection<TechnicianDto> Technicians { get; set; } = new();

        private int? _leadTechnicianId;
        public int? LeadTechnicianId
        {
            get => _leadTechnicianId;
            set
            {
                _leadTechnicianId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LeadTechnicianName));
                OnPropertyChanged(nameof(LeadText));
            }
        }

        public string? LeadTechnicianName => LeadTechnicianId.HasValue
                ? Technicians.FirstOrDefault(t => t.Id == LeadTechnicianId.Value)?.Name
                : null;

        public string LeadText => string.IsNullOrWhiteSpace(LeadTechnicianName)
                ? "Lead: Not set"
                : $"Lead: {LeadTechnicianName}";
    }

    public sealed class LeadStarBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var gray = new SolidColorBrush(Color.FromRgb(107, 114, 128));
            var yellow = new SolidColorBrush(Color.FromRgb(250, 204, 21));

            if (values.Length < 2)
                return gray;

            var techId = TryToInt(values[0]);
            var leadTechId = TryToInt(values[1]);

            if (techId.HasValue &&
                leadTechId.HasValue &&
                techId.Value == leadTechId.Value)
            {
                return yellow;
            }

            return gray;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static int? TryToInt(object? value)
        {
            if (value == null)
                return null;

            if (value is int i)
                return i;

            if (value is uint ui)
                return unchecked((int)ui);

            if (int.TryParse(value.ToString(), out var parsed))
                return parsed;

            return null;
        }
    }

    public sealed class TruckBoardRowHeightConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is not double viewportHeight ||
                viewportHeight <= 0)
            {
                return 1.0;
            }

            /*
             * The board historically shows exactly three equal-height rows.
             *
             * Each ContentPresenter also has an 8px bottom margin,
             * so subtract that from the usable card height.
             */
            return Math.Max(
                1.0,
                (viewportHeight / 3.0) - 8.0);
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}