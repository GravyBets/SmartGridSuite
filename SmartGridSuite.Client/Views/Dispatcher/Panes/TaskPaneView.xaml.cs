using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SmartGridSuite.Client.Models.Dispatcher;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class TasksPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ObservableCollection<DispatchTask> _tasks = new();
        public ICollectionView TasksView { get; }

        private DispatchTask? _selectedTask;
        public DispatchTask? SelectedTask
        {
            get => _selectedTask;
            set
            {
                if (ReferenceEquals(_selectedTask, value)) return;
                _selectedTask = value;
                OnPropertyChanged(nameof(SelectedTask));
            }
        }

        public TasksPaneView()
        {
            InitializeComponent();
            DataContext = this;

            SeedFakeData();

            TasksView = CollectionViewSource.GetDefaultView(_tasks);
            TasksView.Filter = FilterTask;

            // Default sort: newest first
            TasksView.SortDescriptions.Clear();
            TasksView.SortDescriptions.Add(new SortDescription(nameof(DispatchTask.OccurredAt), ListSortDirection.Descending));

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
        }

        // Helpers to generate realistic IDs (9 digits, fixed prefixes)
        private static string MakeNineDigit(string prefix4, int last5)
            => $"{prefix4}{last5:00000}"; // 4 + 5 = 9 digits

        private static bool IsNotification(string? s)
            => !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s, @"^1405\d{5}$");

        private static bool IsWorkOrder(string? s)
            => !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s, @"^1214\d{5}$");

        private void SeedFakeData()
        {
            _tasks.Clear();
            var now = DateTime.Now;

            _tasks.Add(new DispatchTask
            {
                OccurredAt = now.AddDays(-2).AddHours(-1),
                Site = "G1234",
                Tech = "J. Smith",
                Notification = MakeNineDigit("1405", 22119),
                WorkOrder = MakeNineDigit("1214", 88112),
                WorkOrderClass = WorkOrderClass.Capital,
                ActionRequired = "Convert WO Maint → Capital; notify finance",
                Notes = "Tech swapped a radio with a capital asset. Dispatch needs WO conversion + ticket note update.",
                Status = "Open",
                Category = "WO Conversion"
            });

            _tasks.Add(new DispatchTask
            {
                OccurredAt = now.AddDays(-1).AddHours(-3),
                Site = "1234MR",
                Tech = "R. Sanchez",
                Notification = MakeNineDigit("1405", 22588),
                WorkOrder = MakeNineDigit("1214", 88143),
                WorkOrderClass = WorkOrderClass.Maintenance,
                ActionRequired = "Finalize write-up; attach photos; close loop",
                Notes = "Write-up received. Photos still missing. Confirm signals stable before closing.",
                Status = "Waiting",
                Category = "Finalize Notes"
            });

            _tasks.Add(new DispatchTask
            {
                OccurredAt = now.AddDays(-5).AddHours(-2),
                Site = "RX1234",
                Tech = "M. Lopez",
                Notification = MakeNineDigit("1405", 21410),
                WorkOrder = MakeNineDigit("1214", 88044),
                WorkOrderClass = WorkOrderClass.Maintenance,
                ActionRequired = "Order modem; coordinate revisit",
                Notes = "Modem intermittent. Verify part number, ship to warehouse, then schedule revisit.",
                Status = "Open",
                Category = "Parts Follow-up"
            });

            _tasks.Add(new DispatchTask
            {
                OccurredAt = now.AddDays(-7).AddHours(-4),
                Site = "DACs 1029",
                Tech = "P. Garcia",
                Notification = MakeNineDigit("1405", 20332),
                WorkOrder = "", // no WO
                WorkOrderClass = WorkOrderClass.Maintenance,
                ActionRequired = "Verify comms stable; close notification",
                Notes = "No WO tied. Confirm 24h comms/alarms are clean, then close out.",
                Status = "Open",
                Category = "Closeout"
            });

            _tasks.Add(new DispatchTask
            {
                OccurredAt = now.AddHours(-2),
                Site = "G0999",
                Tech = "E. Robinson",
                Notification = MakeNineDigit("1405", 22601),
                WorkOrder = "", // no WO yet
                WorkOrderClass = WorkOrderClass.Maintenance,
                ActionRequired = "Call ops center; update ETA + notes",
                Notes = "Ops escalation in progress. Need ETA and coordination notes logged in the notification.",
                Status = "Blocked",
                Category = "Escalation"
            });

            // Optional: quick sanity checks during fake-data phase
            // (Leave commented or remove later)
            // foreach (var t in _tasks)
            // {
            //     if (!IsNotification(t.Notification)) MessageBox.Show($"Bad Notification: {t.Notification}");
            //     if (!string.IsNullOrWhiteSpace(t.WorkOrder) && !IsWorkOrder(t.WorkOrder)) MessageBox.Show($"Bad WO: {t.WorkOrder}");
            // }
        }

        private bool FilterTask(object obj)
        {
            if (obj is not DispatchTask t) return false;

            var status = StatusFilter?.SelectedItem as string ?? "All";
            if (status != "All" && !string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase))
                return false;

            var cat = CategoryFilter?.SelectedItem as string ?? "All";
            if (cat != "All" && !string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase))
                return false;

            var q = (SearchBox?.Text ?? "").Trim();
            if (q.Length == 0) return true;

            bool match(string? s) => !string.IsNullOrWhiteSpace(s) &&
                                     s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            var timeString = t.OccurredAt.ToString("MM/dd HH:mm");

            return match(timeString) ||
                   match(t.Site) ||
                   match(t.Tech) ||
                   match(t.Notification) ||
                   match(t.WorkOrder) ||
                   match(t.WorkOrderClassLabel) ||
                   match(t.ActionRequired) ||
                   match(t.Notes) ||
                   match(t.Status) ||
                   match(t.Category);
        }

        private void RefreshView() => TasksView?.Refresh();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshView();

        private void Filters_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshView();

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            var keep = SelectedTask?.Notification;
            RefreshView();

            if (!string.IsNullOrWhiteSpace(keep))
            {
                var found = _tasks.FirstOrDefault(x => x.Notification == keep);
                if (found != null)
                    TasksGrid.SelectedItem = found;
            }
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
            }
            else
            {
                DetailsCol.Width = new GridLength(440);
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

        private void CopySummary_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null) return;

            var t = SelectedTask;

            var text =
                $"{t.Site} — {t.Notification}\n" +
                $"{t.OccurredAt:MM/dd/yyyy HH:mm}\n" +
                $"Tech: {t.Tech}\n" +
                $"WO: {t.WorkOrder} {(string.IsNullOrWhiteSpace(t.WorkOrder) ? "" : $"({t.WorkOrderClassLabel})")}\n\n" +
                $"Action Required: {t.ActionRequired}\n\n" +
                $"{t.Notes}";

            Clipboard.SetText(text);
        }

        private void OpenNotification_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null) return;

            MessageBox.Show(
                $"Open notification: {SelectedTask.Notification}\n(later: route to Tickets/Notifications pane + load record)",
                "Open Notification",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void MarkDone_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTask == null) return;

            SelectedTask.Status = "Done";
            RefreshView();

            MessageBox.Show($"Marked {SelectedTask.Notification} as Done (fake).",
                "Task Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

   

    
}