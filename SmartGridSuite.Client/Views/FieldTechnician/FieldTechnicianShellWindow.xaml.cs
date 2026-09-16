using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using SmartGridSuite.Client.Views.FieldTechnician.Panes;
using SmartGridSuite.Contracts.FieldTechnician;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.FieldTechnician
{
    public partial class FieldTechnicianShellWindow
    {
        private readonly ApiClient _connectivityApi = ClientAppSettings.CreateApiClient();

        private readonly MediaPlayer _taskNotificationPlayer = new();
        private readonly DispatcherTimer _taskBadgeTimer;
        private readonly HashSet<long> _knownTaskIds = new();
        private readonly CancellationTokenSource _taskBadgeLifetime = new();

        private bool _hasTaskBadgeBaseline;
        private bool _taskBadgeRefreshInProgress;
        private bool _taskBadgeRefreshRequested;
        private bool _isClosed;

        private bool _navCollapsed;
        private bool _syncingNav;

        private const double NavExpandedWidth = 260;
        private const double NavCollapsedWidth = 58;

        private FieldTechTasksPaneView? _tasksPaneView;
        private FieldTechHistoryPaneView? _historyPaneView;

        // Reuse the existing dashboard so all tab/session/pop-out logic stays shared.
        private SiteDashboardPaneView? _siteDashboardPaneView;

        public FieldTechnicianShellWindow()
        {
            InitializeComponent();

            ConnectivityService.StateChanged += ConnectivityService_StateChanged;

            Closing += FieldTechnicianShellWindow_Closing;
            Closed += FieldTechnicianShellWindow_Closed;

            ApplyConnectivityState(
                ConnectivityService.CurrentState,
                ConnectivityService.CurrentMessage);

            _navCollapsed = true;
            ApplyNavState();

            _taskBadgeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };

            _taskBadgeTimer.Tick += TaskBadgeTimer_Tick;
            _taskBadgeTimer.Start();

            // Default selection = Site Dashboard
            SelectNavIndex(1);

            // Establish the initial Daily Assignment snapshot silently.
            _ = UpdateTaskBadgeAsync();
        }

        private void SelectNavIndex(int index)
        {
            _syncingNav = true;

            NavListExpanded.SelectedIndex = index;
            NavListCollapsed.SelectedIndex = index;

            _syncingNav = false;

            if (index >= 0 &&
                index < NavListExpanded.Items.Count &&
                NavListExpanded.Items[index] is ListBoxItem item)
            {
                ShowPane(item);
            }
        }

        private static string? GetNavKey(ListBoxItem item)
        {
            return item.Tag?.ToString()
                   ?? item.ToolTip?.ToString()
                   ?? item.Content?.ToString();
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingNav)
                return;

            if (sender is not ListBox lb)
                return;

            if (lb.SelectedItem is not ListBoxItem item)
                return;

            _syncingNav = true;

            if (lb == NavListExpanded)
                NavListCollapsed.SelectedIndex = lb.SelectedIndex;
            else
                NavListExpanded.SelectedIndex = lb.SelectedIndex;

            _syncingNav = false;

            ShowPane(item);
        }

        private void ShowPane(ListBoxItem item)
        {
            switch (GetNavKey(item))
            {
                case "Site Dashboard":
                    MainPaneHost.Content = GetOrCreateSiteDashboardPane();
                    break;

                case "Tasks":
                    {
                        var tasksPane =
                            GetOrCreateTasksPane();

                        MainPaneHost.Content =
                            tasksPane;

                        /*
                         * Daily Assignment lifecycle may have changed while the
                         * technician was working in Site Dashboard.
                         *
                         * Refresh every time Tasks becomes active so a completed
                         * assignment immediately moves out of Daily Assignments and,
                         * when the technician is still attached to the ticket, can
                         * appear under Other Assigned Tickets.
                         */
                        _ = tasksPane.RefreshAsync();
                        _ = UpdateTaskBadgeAsync();

                        break;
                    }

                case "History":
                    _historyPaneView ??= new FieldTechHistoryPaneView();
                    MainPaneHost.Content = _historyPaneView;
                    break;

                default:
                    MainPaneHost.Content = GetOrCreateSiteDashboardPane();
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

            if (NavHeaderArrowPath.RenderTransform is RotateTransform rotate)
            {
                rotate.Angle = _navCollapsed ? 0 : 180;
            }

            NavHeaderBtn.ToolTip = _navCollapsed
                ? "Expand navigation"
                : "Collapse navigation";
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TaskBadgeTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _ = UpdateTaskBadgeAsync();
        }

        private void TasksPane_TaskIdsRefreshed(
            IReadOnlyCollection<long> taskIds)
        {
            /*
             * The Tasks pane reports both Daily Assignments and Other Assigned
             * Tickets. The nav badge intentionally represents Daily Assignments only,
             * so use this event merely as a refresh signal and let the shell re-read
             * the API using the DailyAssignments collection below.
             */
            _ = UpdateTaskBadgeAsync();
        }

        private async Task UpdateTaskBadgeAsync()
        {
            if (_isClosed)
                return;

            _taskBadgeRefreshRequested = true;

            if (_taskBadgeRefreshInProgress)
                return;

            _taskBadgeRefreshInProgress = true;

            try
            {
                /*
                 * Coalesce navigation/timer refresh signals so only one task request
                 * runs at a time. A signal arriving during a request causes one
                 * follow-up read after the current request completes.
                 */
                while (_taskBadgeRefreshRequested &&
                       !_isClosed)
                {
                    _taskBadgeRefreshRequested = false;

                    var technician =
                        await CurrentUserService
                            .LoadCurrentTechnicianAsync(
                                forceRefresh: false);

                    if (technician == null ||
                        string.IsNullOrWhiteSpace(
                            technician.EmployeeId))
                    {
                        UpdateTaskBadgeVisuals(0);
                        continue;
                    }

                    var employeeId =
                        Uri.EscapeDataString(
                            technician.EmployeeId);

                    var response =
                        await _connectivityApi
                            .GetAsync<FieldTechTasksResponseDto>(
                                $"api/tickets/field-tech/tasks/{employeeId}",
                                _taskBadgeLifetime.Token);

                    if (_isClosed ||
                        response == null)
                    {
                        return;
                    }

                    /*
                     * Only dispatcher-published Daily Assignments participate in the
                     * nav badge and notification sound. Other Assigned Tickets remain
                     * visible in the Tasks pane but are intentionally ignored here.
                     */
                    var taskIds =
                        response.DailyAssignments
                            .Where(x => x.Id > 0)
                            .Select(x => x.Id)
                            .Distinct()
                            .ToList();

                    var newTasksArrived =
                        ApplyTaskSnapshot(taskIds);

                    /*
                     * If Tasks is already visible, put newly detected Daily
                     * Assignments into the grids immediately instead of waiting for
                     * the technician to click Refresh.
                     */
                    if (newTasksArrived &&
                        _tasksPaneView != null &&
                        ReferenceEquals(
                            MainPaneHost.Content,
                            _tasksPaneView))
                    {
                        _ = _tasksPaneView.RefreshAsync();
                    }
                }
            }
            catch (OperationCanceledException)
                when (_taskBadgeLifetime.IsCancellationRequested)
            {
                // Closing the shell cancels any in-flight task check.
            }
            catch (Exception ex)
            {
                /*
                 * Weak or missing field connectivity must not erase the last known
                 * badge/task snapshot. The next timer tick retries automatically.
                 */
                System.Diagnostics.Debug.WriteLine(
                    "[FieldTechTasksBadge] Refresh failed: " +
                    ex.Message);
            }
            finally
            {
                _taskBadgeRefreshInProgress = false;
            }
        }

        private bool ApplyTaskSnapshot(
            IReadOnlyCollection<long> taskIds)
        {
            var currentIds =
                taskIds
                    .Where(x => x > 0)
                    .ToHashSet();

            var hasNewTasks =
                _hasTaskBadgeBaseline &&
                currentIds.Except(_knownTaskIds).Any();

            _knownTaskIds.Clear();

            foreach (var id in currentIds)
                _knownTaskIds.Add(id);

            UpdateTaskBadgeVisuals(
                currentIds.Count);

            /*
             * Opening Field Technician never makes noise for Daily Assignments that
             * were already present. The first successful snapshot is a silent baseline.
             */
            if (!_hasTaskBadgeBaseline)
            {
                _hasTaskBadgeBaseline = true;
                return false;
            }

            /*
             * One sound per snapshot/batch, regardless of whether one Daily
             * Assignment or twenty new Daily Assignment IDs appeared together.
             */
            if (hasNewTasks)
                PlayTaskNotificationSound();

            return hasNewTasks;
        }

        private void UpdateTaskBadgeVisuals(
            int count)
        {
            var visibility =
                count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            var displayText =
                count > 99
                    ? "99+"
                    : count.ToString();

            FieldTechTasksBadgeExpanded.Visibility =
                visibility;

            FieldTechTasksBadgeCollapsed.Visibility =
                visibility;

            FieldTechTasksBadgeExpandedText.Text =
                displayText;

            FieldTechTasksBadgeCollapsedText.Text =
                displayText;
        }

        private void PlayTaskNotificationSound()
        {
            try
            {
                var path =
                    System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Assets",
                        "Sounds",
                        "MessageTone.mp3");

                if (System.IO.File.Exists(path))
                {
                    _taskNotificationPlayer.Open(
                        new Uri(
                            path,
                            UriKind.Absolute));

                    _taskNotificationPlayer.Volume = 1.0;
                    _taskNotificationPlayer.Position =
                        TimeSpan.Zero;

                    _taskNotificationPlayer.Play();

                    _taskNotificationPlayer.MediaEnded -=
                        TaskNotificationPlayer_MediaEnded;

                    _taskNotificationPlayer.MediaEnded +=
                        TaskNotificationPlayer_MediaEnded;

                    _taskNotificationPlayer.MediaFailed -=
                        TaskNotificationPlayer_MediaFailed;

                    _taskNotificationPlayer.MediaFailed +=
                        TaskNotificationPlayer_MediaFailed;

                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[FieldTechTasksBadge] Sound failed: " +
                    ex.Message);
            }

            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch
            {
            }
        }

        private void TaskNotificationPlayer_MediaEnded(
            object? sender,
            EventArgs e)
        {
            try
            {
                _taskNotificationPlayer.Stop();
                _taskNotificationPlayer.Close();
            }
            catch
            {
            }
        }

        private void TaskNotificationPlayer_MediaFailed(
            object? sender,
            ExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                "[FieldTechTasksBadge] Media failed: " +
                e.ErrorException?.Message);

            try
            {
                _taskNotificationPlayer.Close();
            }
            catch
            {
            }
        }

        private FieldTechTasksPaneView GetOrCreateTasksPane()
        {
            if (_tasksPaneView != null)
                return _tasksPaneView;

            _tasksPaneView = new FieldTechTasksPaneView();

            _tasksPaneView.TaskIdsRefreshed +=
                TasksPane_TaskIdsRefreshed;

            _tasksPaneView.OpenTicketRequested += async ticket =>
            {
                await OpenTicketsInDashboardAsync(
                    new[] { ticket });
            };

            _tasksPaneView.OpenAllTicketsRequested += async tickets =>
            {
                await OpenTicketsInDashboardAsync(tickets);
            };

            return _tasksPaneView;
        }

        private async Task OpenTicketsInDashboardAsync(IEnumerable<FieldTechTicketListItemDto> tickets)
        {
            var cleanTickets = tickets
                .Where(x =>
                    x != null &&
                    x.Id > 0 &&
                    !string.IsNullOrWhiteSpace(x.Site))
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList();

            if (cleanTickets.Count == 0)
                return;

            SelectNavIndex(0);

            var dashboard =
                GetOrCreateSiteDashboardPane();

            MainPaneHost.Content =
                dashboard;

            await dashboard.OpenTicketsFromFieldTechTasksAsync(
                cleanTickets);
        }

        private SiteDashboardPaneView GetOrCreateSiteDashboardPane()
        {
            if (_siteDashboardPaneView != null)
                return _siteDashboardPaneView;

            _siteDashboardPaneView = new SiteDashboardPaneView
            {
                CanManageSiteNotes = false
            };

            return _siteDashboardPaneView;
        }

        // Receives application-wide connection changes and safely updates this window
        // even when the originating API request completed on another thread.
        private void ConnectivityService_StateChanged(
            object? sender,
            ConnectivityChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() =>
                    ApplyConnectivityState(
                        e.State,
                        e.Message));

                return;
            }

            ApplyConnectivityState(
                e.State,
                e.Message);
        }

        // Shows connection problems persistently without blocking the technician with
        // repeated modal windows.
        private void ApplyConnectivityState(
            ConnectivityState state,
            string message)
        {
            var shouldShow =
                state == ConnectivityState.Offline ||
                state == ConnectivityState.Degraded ||
                state == ConnectivityState.Checking;

            ConnectivityBanner.Visibility =
                shouldShow
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ConnectivityMessageText.Text =
                string.IsNullOrWhiteSpace(message)
                    ? "Unable to determine server availability."
                    : message;

            ConnectivityRetryButton.IsEnabled =
                state != ConnectivityState.Checking;

            ConnectivityRetryButton.Content =
                state == ConnectivityState.Checking
                    ? "Checking..."
                    : "Retry";
        }

        // Calls the lightweight health endpoint and restores normal UI state once both
        // the API and database are available again.
        private async void RetryConnectivity_Click(
            object sender,
            RoutedEventArgs e)
        {
            ConnectivityService.BeginCheck();

            try
            {
                var result =
                    await _connectivityApi.GetAsync<ApiHealthResponse>(
                        "api/health");

                if (result?.ApiAvailable == true &&
                    result.DatabaseAvailable)
                {
                    ConnectivityService.ReportOnline();
                    return;
                }

                ConnectivityService.ReportDegraded(
                    "The API is reachable, but the Smart Grid database is unavailable.");
            }
            catch (ApiClient.ApiConnectionException)
            {
                /*
                 * ApiClient already reported the offline state. No modal window is
                 * needed because the persistent banner displays the result.
                 */
            }
            catch (ApiClient.ApiException ex)
            {
                ConnectivityService.ReportDegraded(
                    $"The health check returned server error {ex.StatusCode}.");
            }
        }

        // Removes shared subscriptions and notification resources when this shell closes.
        private void FieldTechnicianShellWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _isClosed = true;

            _taskBadgeTimer.Stop();
            _taskBadgeTimer.Tick -=
                TaskBadgeTimer_Tick;

            _taskBadgeLifetime.Cancel();
            _taskBadgeLifetime.Dispose();

            if (_tasksPaneView != null)
            {
                _tasksPaneView.TaskIdsRefreshed -=
                    TasksPane_TaskIdsRefreshed;
            }

            _taskNotificationPlayer.MediaEnded -=
                TaskNotificationPlayer_MediaEnded;

            _taskNotificationPlayer.MediaFailed -=
                TaskNotificationPlayer_MediaFailed;

            try
            {
                _taskNotificationPlayer.Stop();
                _taskNotificationPlayer.Close();
            }
            catch
            {
            }

            ConnectivityService.StateChanged -=
                ConnectivityService_StateChanged;
        }

        private void FieldTechnicianShellWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (_siteDashboardPaneView is null)
                return;

            if (_siteDashboardPaneView
                .ConfirmDiscardWriteUpsForShellClose(this))
            {
                return;
            }

            e.Cancel = true;
        }

        private sealed class ApiHealthResponse
        {
            public bool ApiAvailable { get; set; }

            public bool DatabaseAvailable { get; set; }

            public DateTimeOffset CheckedAtUtc { get; set; }
        }
    }
}