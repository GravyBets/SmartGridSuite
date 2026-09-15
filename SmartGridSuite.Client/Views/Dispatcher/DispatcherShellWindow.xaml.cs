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
using System.Threading;

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

        // Badge counts always come from the API, independently of the visible pane.
        private readonly TicketsApi _ticketsApi;

        // Sound playback helpers
        private readonly MediaPlayer _notificationPlayer = new();

        // When true, the next PendingTaskCount assignment will NOT play the notification sound.
        // Used when the shell reconciles with TaskPane in-memory state (no audible feedback desired).
        private bool _suppressNotificationSound = false;
        private bool _hasTaskCountBaseline;
        private bool _badgeRefreshInProgress;
        private bool _badgeRefreshRequested;
        private bool _isClosed;
        private readonly CancellationTokenSource _badgeLifetime = new();

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

            // ShowPane can request a badge refresh during initial navigation.
            _ticketsApi = new TicketsApi(_api);

            SelectNavIndex(1);

            DataContext = this;

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

                    try
                    {
                        _taskPaneView.PendingTaskCountChanged -=
                            TaskPaneView_PendingTaskCountChanged;

                        _taskPaneView.PendingTaskCountChanged +=
                            TaskPaneView_PendingTaskCountChanged;

                        // Never replace the global count with the pane's filtered rows.
                        _ = UpdatePendingTaskCountAsync();
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

        // A pane reload signals that server data may have changed. Its count is
        // intentionally ignored because the pane can be filtered by search/status.
        private void TaskPaneView_PendingTaskCountChanged(
            object? sender,
            int count)
        {
            if (_isClosed)
                return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    _ = UpdatePendingTaskCountAsync()));
                return;
            }

            _ = UpdatePendingTaskCountAsync();
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
        
        // Use the API's unfiltered total, not the current grid or a capped page
        // of rows. The API owns which configured statuses belong in Tasks.
        private async Task UpdatePendingTaskCountAsync()
        {
            if (_isClosed)
                return;

            _badgeRefreshRequested = true;
            if (_badgeRefreshInProgress)
                return;

            _badgeRefreshInProgress = true;
            try
            {
                // Coalesce refresh signals arriving during a request into one
                // follow-up read so an older response cannot overwrite newer data.
                while (_badgeRefreshRequested && !_isClosed)
                {
                    _badgeRefreshRequested = false;

                    var request = new DispatchTaskQueryRequest
                    {
                        Search = null,
                        Statuses = new List<string>(),
                        ApplyStatusFilter = false,
                        AssignedTech = "All",
                        From = null,
                        To = null,
                        Skip = 0,
                        Take = 1
                    };

                    var response = await _ticketsApi.QueryDispatchTasksAsync(
                        request, _badgeLifetime.Token);

                    if (_isClosed)
                        return;

                    // First successful load establishes a silent baseline.
                    // Only subsequent increases in the global count play a tone.
                    AssignPendingTaskCount(
                        response.TotalCount,
                        suppressSound: !_hasTaskCountBaseline);
                    _hasTaskCountBaseline = true;
                }
            }
            catch (OperationCanceledException) when (_isClosed)
            {
                // Closing the shell cancels its outstanding badge request.
            }
            catch (Exception ex)
            {
                // Preserve the last successful count on a connection failure.
                // The next timer tick (or pane reload) will retry.
                System.Diagnostics.Debug.WriteLine(
                    $"[BadgeTimer] Task count refresh failed: {ex.Message}");
            }
            finally
            {
                _badgeRefreshInProgress = false;
            }
        }

        private void DispatcherShellWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _isClosed = true;
            _badgeLifetime.Cancel();
            _badgeLifetime.Dispose();

            if (_taskPaneView != null)
                _taskPaneView.PendingTaskCountChanged -= TaskPaneView_PendingTaskCountChanged;

            _notificationPlayer.MediaEnded -= NotificationPlayer_MediaEnded;
            _notificationPlayer.MediaFailed -= NotificationPlayer_MediaFailed;
            _notificationPlayer.Close();

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