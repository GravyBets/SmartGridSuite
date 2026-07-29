using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using SmartGridSuite.Client.Views.FieldTechnician.Panes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.FieldTechnician
{
    public partial class FieldTechnicianShellWindow
    {
        private readonly ApiClient _connectivityApi = ClientAppSettings.CreateApiClient();

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

            Closed += FieldTechnicianShellWindow_Closed;

            ApplyConnectivityState(
                ConnectivityService.CurrentState,
                ConnectivityService.CurrentMessage);

            _navCollapsed = true;
            ApplyNavState();

            // Default selection = Site Dashboard
            SelectNavIndex(1);
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
                    MainPaneHost.Content = GetOrCreateTasksPane();
                    break;

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

        private FieldTechTasksPaneView GetOrCreateTasksPane()
        {
            if (_tasksPaneView != null)
                return _tasksPaneView;

            _tasksPaneView = new FieldTechTasksPaneView();

            _tasksPaneView.OpenSiteRequested += async (_, site) =>
            {
                await OpenSitesInDashboardAsync(new[] { site });
            };

            _tasksPaneView.OpenAllSitesRequested += async (_, sites) =>
            {
                await OpenSitesInDashboardAsync(sites);
            };

            return _tasksPaneView;
        }

        private async Task OpenSitesInDashboardAsync(IEnumerable<string> sites)
        {
            var cleanSites = sites
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanSites.Count == 0)
                return;

            SelectNavIndex(0);

            var dashboard = GetOrCreateSiteDashboardPane();
            MainPaneHost.Content = dashboard;

            await dashboard.OpenSitesFromFieldTechTasksAsync(cleanSites);
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

        // Removes the shared event subscription when this shell closes.
        private void FieldTechnicianShellWindow_Closed(
            object? sender,
            EventArgs e)
        {
            ConnectivityService.StateChanged -=
                ConnectivityService_StateChanged;
        }

        private sealed class ApiHealthResponse
        {
            public bool ApiAvailable { get; set; }

            public bool DatabaseAvailable { get; set; }

            public DateTimeOffset CheckedAtUtc { get; set; }
        }
    }
}