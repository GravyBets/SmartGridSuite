using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using System.ComponentModel;
using SmartGridSuite.Client.Services;
using System.Windows.Threading;
using System.Windows.Data;
using System.Globalization;
using SmartGridSuite.Contracts.Dispatcher;
using System.Media;

namespace SmartGridSuite.Client.Views
{
    public partial class DispatcherShellWindow : INotifyPropertyChanged
    {
        private bool _navCollapsed;
        private bool _syncingNav;
        private SiteDashboardPaneView? _siteDashboardPaneView;
        private TaskPaneView? _taskPaneView;
        private TicketsPaneView? _ticketsPaneView;
        private TechniciansPaneView? _techniciansPaneView;
        private DailyAssignmentsPaneView? _dailyAssignmentsPaneView;
        private SiteHistoryPaneView? _siteHistoryPaneView;

        private int _currentNavIndex;
        private bool _allowCloseWithoutPrompt;
        private bool _closePromptRunning;

        private const double NavExpandedWidth = 260;
        private const double NavCollapsedWidth = 58;

        private readonly ApiClient _api = ClientAppSettings.CreateApiClient();

        // Tickets API used by the shell fallback poll when TaskPaneView is not instantiated.
        private readonly TicketsApi _ticketsApi;

        // Sound playback helpers
        private readonly MediaPlayer _notificationPlayer = new();

        // When true, the next PendingTaskCount assignment will NOT play the notification sound.
        // Used when the shell reconciles with TaskPane in-memory state (no audible feedback desired).
        private bool _suppressNotificationSound = false;
        private bool _suppressNextTaskPaneCountNotification;

        // Example path: Assets/Sounds/MessageTone.mp3
        private const string NotificationSoundPackUri = "pack://application:,,,/Assets/Sounds/MessageTone.mp3";

        // Badge timer for nav skittle (updates every 60s)
        private readonly DispatcherTimer _badgeTimer;

        // INotifyPropertyChanged implementation for simple bindings
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private int _pendingTaskCount;
        public int PendingTaskCount
        {
            get => _pendingTaskCount;
            set
            {
                if (_pendingTaskCount == value) return;
                _pendingTaskCount = value;
                OnPropertyChanged(nameof(PendingTaskCount));

                // Only play sound here if some other code set it and you want immediate tone.
                // Prefer using AssignPendingTaskCount to control tone on increase.
                if (!_suppressNotificationSound)
                {
                    // Optional: fallback play if AssignPendingTaskCount wasn't used.
                    // PlayNotificationTone();
                }
            }
        }

        // Helper to assign the pending task count with explicit control over sound suppression.
        // Plays the notification tone only when the count increases (unless suppressed).
        private void AssignPendingTaskCount(int newCount, bool suppressSound)
        {
            // Capture previous value for increase detection using backing field to avoid races.
            var previous = _pendingTaskCount;

            System.Diagnostics.Debug.WriteLine($"[AssignPendingTaskCount] previous={previous} new={newCount} suppress={suppressSound} at {DateTime.UtcNow:O}");

            if (suppressSound)
            {
                _suppressNotificationSound = true;
            }

            // Assign via the property so bindings update
            PendingTaskCount = newCount;

            // If suppression was set, consume it and do not play sound here
            if (suppressSound)
            {
                _suppressNotificationSound = false;
                System.Diagnostics.Debug.WriteLine("[AssignPendingTaskCount] suppressed sound for reconciliation");
                return;
            }

            // Play tone only when count increased
            if (newCount > previous)
            {
                System.Diagnostics.Debug.WriteLine("[AssignPendingTaskCount] count increased — playing notification sound");
                PlayNotificationSound();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[AssignPendingTaskCount] no increase — no sound");
            }
        }

        // Simple converters used by the XAML badge bindings
        // Int -> Visibility (Visible when > 0)
        public class IntToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int i && i > 0)
                    return Visibility.Visible;
                return Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        // Int -> string with cap (e.g., 99+)
        public class TaskCountCapConverter : IValueConverter
        {
            public int Cap { get; set; } = 99;

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int i)
                {
                    if (i <= Cap) return i.ToString();
                    return $"{Cap}+";
                }

                return "0";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        public DispatcherShellWindow()
        {
            InitializeComponent();

            Closing += DispatcherShellWindow_Closing;
            Closed += DispatcherShellWindow_Closed;

            _navCollapsed = true;
            ApplyNavState();

            SelectNavIndex(1);

            DataContext = this;

            // Initialize TicketsApi for shell-level polling fallback
            _ticketsApi = new TicketsApi(_api);

            // Initialize and start the badge timer (60 seconds)
            _badgeTimer = new DispatcherTimer
            {
                // Shorter interval for testing; revert to 60s after debugging.
                Interval = TimeSpan.FromSeconds(60)
            };
            _badgeTimer.Tick += BadgeTimer_Tick;
            _badgeTimer.Start();
                        
            // Do an immediate initial update
            _ = UpdatePendingTaskCountAsync();
        }

        private void SelectNavIndex(int index)
        {
            _syncingNav = true;

            NavListExpanded.SelectedIndex = index;
            NavListCollapsed.SelectedIndex = index;

            _syncingNav = false;

            if (index >= 0 && index < NavListExpanded.Items.Count &&
                NavListExpanded.Items[index] is ListBoxItem item)
            {
                ShowPane(item);
                _currentNavIndex = index;
            }            
        }

        private static string? GetNavKey(ListBoxItem item)
        {
            return item.Tag?.ToString()
                   ?? item.ToolTip?.ToString()
                   ?? item.Content?.ToString();
        }

        private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingNav)
                return;

            if (sender is not ListBox lb)
                return;

            if (lb.SelectedItem is not ListBoxItem item)
                return;

            var requestedIndex = lb.SelectedIndex;

            var canLeave = await ConfirmCurrentPaneCanCloseAsync();

            if (!canLeave)
            {
                _syncingNav = true;

                NavListExpanded.SelectedIndex = _currentNavIndex;
                NavListCollapsed.SelectedIndex = _currentNavIndex;

                _syncingNav = false;
                return;
            }

            _syncingNav = true;

            if (lb == NavListExpanded)
                NavListCollapsed.SelectedIndex = requestedIndex;
            else
                NavListExpanded.SelectedIndex = requestedIndex;

            _syncingNav = false;

            ShowPane(item);
            _currentNavIndex = requestedIndex;
        }

        private async Task<bool> ConfirmCurrentPaneCanCloseAsync()
        {
            if (MainPaneHost.Content is TechniciansPaneView techniciansPane)
                return await techniciansPane.ConfirmLeaveIfDirtyAsync();

            return true;
        }

        private void ShowPane(ListBoxItem item)
        {
            switch (GetNavKey(item))
            {
                case "Site Dashboard":
                    _siteDashboardPaneView ??= new SiteDashboardPaneView();
                    MainPaneHost.Content = _siteDashboardPaneView;
                    break;

                case "Tasks":
                    _taskPaneView ??= new TaskPaneView();

                    if (!_taskPaneView.HasLoadedOnce)
                    {
                        _suppressNextTaskPaneCountNotification = true;
                    }

                    try
                    {
                        _taskPaneView.PendingTaskCountChanged -=
                            TaskPaneView_PendingTaskCountChanged;

                        _taskPaneView.PendingTaskCountChanged +=
                            TaskPaneView_PendingTaskCountChanged;

                        AssignPendingTaskCount(
                            _taskPaneView.GetPendingTaskCount(),
                            suppressSound: true);
                    }
                    catch
                    {
                        // Ignore any errors here; the badge timer will update shortly.
                    }

                    MainPaneHost.Content = _taskPaneView;
                    break;

                case "Tickets":
                    _ticketsPaneView ??= new TicketsPaneView();
                    MainPaneHost.Content = _ticketsPaneView;
                    break;

                case "Truck Assignments":
                    _techniciansPaneView ??= new TechniciansPaneView();
                    MainPaneHost.Content = _techniciansPaneView;
                    break;

                case "Daily Assignments":
                    _dailyAssignmentsPaneView ??= new DailyAssignmentsPaneView();
                    MainPaneHost.Content = _dailyAssignmentsPaneView;
                    break;

                case "Site History":
                    _siteHistoryPaneView ??= new SiteHistoryPaneView();
                    MainPaneHost.Content = _siteHistoryPaneView;
                    break;

                default:
                    _siteDashboardPaneView ??= new SiteDashboardPaneView();
                    MainPaneHost.Content = _siteDashboardPaneView;
                    break;
            }
        }

        private void ToggleNav_Click(object sender, RoutedEventArgs e)
        {
            _navCollapsed = !_navCollapsed;
            ApplyNavState();
        }

        private void ApplyNavState()
        {
            NavCol.Width = _navCollapsed
                ? new GridLength(NavCollapsedWidth)
                : new GridLength(NavExpandedWidth);

            NavShellBorder.Padding = _navCollapsed
                ? new Thickness(5)
                : new Thickness(12);

            NavListExpanded.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            NavListCollapsed.Visibility = _navCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

            NavHeaderTextPanel.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            Grid.SetColumn(NavHeaderBtn, _navCollapsed ? 0 : 1);
            Grid.SetColumnSpan(NavHeaderBtn, _navCollapsed ? 2 : 1);

            NavHeaderBtn.HorizontalAlignment = _navCollapsed
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Right;

            NavSectionLabel.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            HomeButtonExpanded.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            HomeButtonCollapsed.Visibility = _navCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (NavHeaderArrowPath?.RenderTransform is RotateTransform rotate)
            {
                rotate.Angle = _navCollapsed ? 0 : 180;
            }

            NavHeaderBtn.ToolTip = _navCollapsed
                ? "Expand navigation"
                : "Collapse navigation";
        }

        private async void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            var canLeave = await ConfirmCurrentPaneCanCloseAsync();

            if (!canLeave)
                return;

            _allowCloseWithoutPrompt = true;

            Close();
        }

        // Handler for TaskPaneView.PendingTaskCountChanged
        private void TaskPaneView_PendingTaskCountChanged(
            object? sender,
            int count)
        {
            void ApplyCount()
            {
                if (_suppressNextTaskPaneCountNotification)
                {
                    _suppressNextTaskPaneCountNotification = false;

                    AssignPendingTaskCount(
                        count,
                        suppressSound: true);

                    return;
                }

                AssignPendingTaskCount(
                    count,
                    suppressSound: false);
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ApplyCount);
                return;
            }

            ApplyCount();
        }

        // Play the notification sound once (non-blocking) using MediaPlayer for MP3 resources.
        // Requires the MP3 to be added to the project as a Resource (Build Action = Resource).
        private void PlayNotificationSound()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "Sounds",
                    "MessageTone.mp3");

                if (System.IO.File.Exists(path))
                {
                    // Use the long-lived field so the MediaPlayer is not GC'd immediately.
                    try
                    {
                        if (!Dispatcher.CheckAccess())
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _notificationPlayer.Open(new Uri(path, UriKind.Absolute));
                                _notificationPlayer.Volume = 1.0;
                                _notificationPlayer.Position = TimeSpan.Zero;
                                _notificationPlayer.Play();
                                System.Diagnostics.Debug.WriteLine("[Sound] _notificationPlayer playing from file path (UI thread)");
                            });
                        }
                        else
                        {
                            _notificationPlayer.Open(new Uri(path, UriKind.Absolute));
                            _notificationPlayer.Volume = 1.0;
                            _notificationPlayer.Position = TimeSpan.Zero;
                            _notificationPlayer.Play();
                            System.Diagnostics.Debug.WriteLine("[Sound] _notificationPlayer playing from file path");
                        }

                        // Ensure we stop/close when finished
                        _notificationPlayer.MediaEnded -= NotificationPlayer_MediaEnded;
                        _notificationPlayer.MediaEnded += NotificationPlayer_MediaEnded;

                        _notificationPlayer.MediaFailed -= NotificationPlayer_MediaFailed;
                        _notificationPlayer.MediaFailed += NotificationPlayer_MediaFailed;

                        return;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Sound] _notificationPlayer play failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Sound] file not found at output path: " + path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sound] PlayNotificationSound(file) failed: {ex.GetType().Name}: {ex.Message}");
            }

            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        // Helper event handlers for the long-lived player
        private void NotificationPlayer_MediaEnded(object? sender, EventArgs e)
        {
            try
            {
                _notificationPlayer.Stop();
                _notificationPlayer.Close();
            }
            catch { }
        }

        private void NotificationPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Sound] MediaFailed: {e.ErrorException?.Message}");
            try { _notificationPlayer.Close(); } catch { }
        }

        // Badge timer tick handler
        private void BadgeTimer_Tick(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[BadgeTimer] tick at {DateTime.UtcNow:O}");
            // Fire-and-forget update on UI thread
            _ = UpdatePendingTaskCountAsync();
        }
        
        // Update the PendingTaskCount property from available sources.
        // Current implementation prefers the in-memory TaskPaneView if instantiated.
        // This method is resilient to exceptions and will silently ignore transient failures.
        private async Task UpdatePendingTaskCountAsync()
        {
            try
            {
                // Use TaskPane in-memory count only when the Task pane is the active content AND it has completed its initial load.
                // If the Task pane exists but is not active, prefer the API fallback so the badge reflects new tasks arriving while the user is on other panes.
                if (_taskPaneView != null && MainPaneHost?.Content == _taskPaneView && _taskPaneView.HasLoadedOnce)
                {
                    try
                    {
                        var inMemory = _taskPaneView.GetPendingTaskCount();
                        System.Diagnostics.Debug.WriteLine($"[BadgeTimer] using TaskPane in-memory count (active pane) = {inMemory}");
                        // When UpdatePendingTaskCountAsync is invoked (e.g., by Refresh), treat this as a live update
                        // and do not suppress the notification sound so arrivals while on Tasks still play.
                        AssignPendingTaskCount(inMemory, suppressSound: false);
                        return;

                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BadgeTimer] TaskPane in-memory count failed: {ex.GetType().Name}: {ex.Message}");
                        // Fall through to API fallback; do not crash the shell for badge updates.
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[BadgeTimer] TaskPane not active or not authoritative; using API fallback");
                }




                // 2) Fallback: query the server for a current task list and count pending items.
                // Build a minimal query request similar to the TaskPane's request.
                try
                {
                    var request = new DispatchTaskQueryRequest
                    {
                        Search = null,
                        Statuses = new List<string>(),
                        ApplyStatusFilter = false,
                        AssignedTech = "All",
                        From = null,
                        To = null,
                        Skip = 0,
                        Take = 2000
                    };

                    System.Diagnostics.Debug.WriteLine("[BadgeTimer] calling QueryDispatchTasksAsync...");
                    var response = await _ticketsApi.QueryDispatchTasksAsync(request);

                    if (response == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[BadgeTimer] response == null");
                    }
                    else
                    {
                        var itemsCount = response.Items?.Count ?? 0;
                        System.Diagnostics.Debug.WriteLine($"[BadgeTimer] API returned {itemsCount} items");
                    }

                    // If the API returns items, count pending ones (same logic as TaskPane).
                    var items = response?.Items ?? new List<DispatchTaskListItemDto>();
                    var pendingCount = items.Count(item =>
                    {
                        var status = (item?.Status ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(status))
                            return true;

                        if (status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                            status.Equals("Canceled", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        return true;
                    });

                    System.Diagnostics.Debug.WriteLine($"[BadgeTimer] computed pendingCount = {pendingCount}");
                    // API fallback represents live server state; play tone for increases.
                    AssignPendingTaskCount(pendingCount, suppressSound: false);
                    return;

                }
                catch (Exception ex)
                {
                    // Log the exception so we can see why the API call failed.
                    System.Diagnostics.Debug.WriteLine($"[BadgeTimer] QueryDispatchTasksAsync failed: {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.StackTrace);
                }

            }
            catch
            {
                // Swallow any unexpected exceptions to avoid impacting the shell UI.
            }

            // Keep method async-friendly
            await Task.CompletedTask;
        }

        private void DispatcherShellWindow_Closed(
            object? sender,
            EventArgs e)
        {
            Closing -= DispatcherShellWindow_Closing;
            Closed -= DispatcherShellWindow_Closed;
            // Stop badge timer
            try
            {
                if (_badgeTimer != null)
                {
                    _badgeTimer.Stop();
                    _badgeTimer.Tick -= BadgeTimer_Tick;
                }
            }
            catch { }

            _siteDashboardPaneView?.Shutdown();

            if (MainPaneHost != null)
                MainPaneHost.Content = null;


            _siteDashboardPaneView = null;
            _taskPaneView = null;
            _ticketsPaneView = null;
            _techniciansPaneView = null;
            _dailyAssignmentsPaneView = null;
            _siteHistoryPaneView = null;
        }

        private async void DispatcherShellWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_allowCloseWithoutPrompt)
                return;

            if (_closePromptRunning)
            {
                e.Cancel = true;
                return;
            }

            if (MainPaneHost.Content is not TechniciansPaneView)
                return;

            e.Cancel = true;
            _closePromptRunning = true;

            try
            {
                var canClose = await ConfirmCurrentPaneCanCloseAsync();

                if (!canClose)
                    return;

                _allowCloseWithoutPrompt = true;

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    Close();
                }), DispatcherPriority.Background);
            }
            finally
            {
                _closePromptRunning = false;
            }
        }
    }
}