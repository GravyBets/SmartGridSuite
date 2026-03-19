using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Dispatcher;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TaskPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ObservableCollection<DispatchTask> _tasks = new();
        private readonly TicketsApi _ticketsApi;

        private bool _hasLoadedOnce;
        private bool _isLoading;

        public ICollectionView TasksView { get; }

        private DispatchTask? _selectedTask;
        public DispatchTask? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (ReferenceEquals(_selectedTask, value))
                    return;

                _selectedTask = value;
                OnPropertyChanged(nameof(SelectedTask));
            }
        }

        public TaskPaneView()
        {
            InitializeComponent();
            DataContext = this;

            _ticketsApi = new TicketsApi(new ApiClient("Https://localhost:7140"));

            TasksView = CollectionViewSource.GetDefaultView(_tasks);
            TasksView.Filter = FilterTask;

            TasksView.SortDescriptions.Clear();
            TasksView.SortDescriptions.Add(
                new SortDescription(nameof(DispatchTask.OccurredAt), ListSortDirection.Descending));

            TasksGrid.ItemsSource = TasksView;

            StatusFilter.ItemsSource = new[] { "All", "Open", "Waiting", "Blocked", "Done" };
            StatusFilter.SelectedIndex = 0;

            CategoryFilter.ItemsSource = new[]
            {
                "All",
                "WO Conversion",
                "Finalize Notes",
                "Parts Follow-up",
                "Closeout",
                "Escalation"
            };
            CategoryFilter.SelectedIndex = 0;

            UpdateDetailsVisibility();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasLoadedOnce)
                return;

            _hasLoadedOnce = true;
            await LoadTasksAsync();
        }

        private async Task LoadTasksAsync()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            var keepNotification = SelectedTask?.Notification;

            try
            {
                var items = await _ticketsApi.GetDispatchTasksAsync();

                _tasks.Clear();

                foreach (var item in items
                             .OrderByDescending(x => x.OccurredAt)
                             .Select(MapDtoToModel))
                {
                    _tasks.Add(item);
                }

                RefreshView();
                RestoreSelection(keepNotification);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load dispatch tasks.\n\n{ex.Message}",
                    "Task Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private static DispatchTask MapDtoToModel(DispatchTaskListItemDto dto)
        {
            return new DispatchTask
            {
                OccurredAt = dto.OccurredAt,
                Site = dto.Site ?? "",
                Tech = dto.Tech ?? "",
                Notification = dto.Notification ?? "",
                WorkOrder = dto.WorkOrder ?? "",
                WorkOrderClass = string.Equals(dto.WorkOrderType, "Capital", StringComparison.OrdinalIgnoreCase)
                    ? WorkOrderClass.Capital
                    : WorkOrderClass.Maintenance,
                ActionRequired = dto.ActionRequired ?? "",
                Notes = dto.Notes ?? "",
                Status = dto.Status ?? "",
                Category = dto.Category ?? ""
            };
        }

        private bool FilterTask(object obj)
        {
            if (obj is not DispatchTask t)
                return false;

            var status = StatusFilter?.SelectedItem as string ?? "All";
            if (status != "All" &&
                !string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var category = CategoryFilter?.SelectedItem as string ?? "All";
            if (category != "All" &&
                !string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var q = (SearchBox?.Text ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;

            static bool Match(string? source, string query) =>
                !string.IsNullOrWhiteSpace(source) &&
                source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            var timeString = t.OccurredAt.ToString("MM/dd HH:mm");

            return Match(timeString, q)
                   || Match(t.Site, q)
                   || Match(t.Tech, q)
                   || Match(t.Notification, q)
                   || Match(t.WorkOrder, q)
                   || Match(t.WorkOrderClassLabel, q)
                   || Match(t.ActionRequired, q)
                   || Match(t.Notes, q)
                   || Match(t.Status, q)
                   || Match(t.Category, q);
        }

        private void RefreshView()
        {
            TasksView.Refresh();

            if (SelectedTask != null && !FilterTask(SelectedTask))
            {
                TasksGrid.SelectedItem = null;
                SelectedTask = null;
            }

            UpdateDetailsVisibility();
        }

        private void RestoreSelection(string? notification)
        {
            if (string.IsNullOrWhiteSpace(notification))
            {
                UpdateDetailsVisibility();
                return;
            }

            var found = _tasks.FirstOrDefault(x =>
                string.Equals(x.Notification, notification, StringComparison.OrdinalIgnoreCase)
                && FilterTask(x));

            if (found != null)
            {
                TasksGrid.SelectedItem = found;
                TasksGrid.ScrollIntoView(found);
            }
            else
            {
                TasksGrid.SelectedItem = null;
                SelectedTask = null;
            }

            UpdateDetailsVisibility();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshView();
        }

        private void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshView();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTasksAsync();
        }

        private void TasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedTask = TasksGrid.SelectedItem as DispatchTask;
            UpdateDetailsVisibility();
        }

        private void CloseDetails_Click(object sender, RoutedEventArgs e)
        {
            TasksGrid.SelectedItem = null;
            SelectedTask = null;
            UpdateDetailsVisibility();
        }

        private void UpdateDetailsVisibility()
        {
            if (SelectedTask == null)
            {
                DetailsPanel.Visibility = Visibility.Collapsed;
                DetailsCol.Width = new GridLength(0);

                if (DetailsSplitter != null)
                    DetailsSplitter.Visibility = Visibility.Collapsed;

                return;
            }

            DetailsCol.Width = new GridLength(440);
            DetailsPanel.Visibility = Visibility.Visible;

            if (DetailsSplitter != null)
                DetailsSplitter.Visibility = Visibility.Visible;
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

        private void CopySummary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null)
                return;

            var t = SelectedTask;

            var workOrderText = string.IsNullOrWhiteSpace(t.WorkOrder)
                ? "(none)"
                : $"{t.WorkOrder} ({t.WorkOrderClassLabel})";

            var text =
                $"{t.Site} — {t.Notification}{Environment.NewLine}" +
                $"{t.OccurredAt:MM/dd/yyyy HH:mm}{Environment.NewLine}" +
                $"Tech: {t.Tech}{Environment.NewLine}" +
                $"Category: {t.Category}{Environment.NewLine}" +
                $"Status: {t.Status}{Environment.NewLine}" +
                $"WO: {workOrderText}{Environment.NewLine}{Environment.NewLine}" +
                $"Action Required: {t.ActionRequired}{Environment.NewLine}{Environment.NewLine}" +
                $"{t.Notes}";

            Clipboard.SetText(text);
        }

        private void OpenNotification_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null)
                return;

            MessageBox.Show(
                $"Open notification: {SelectedTask.Notification}\n\n(next step later)",
                "Open Notification",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void MarkDone_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null)
                return;

            SelectedTask.Status = "Done";
            OnPropertyChanged(nameof(SelectedTask));
            RefreshView();
        }
    }
}