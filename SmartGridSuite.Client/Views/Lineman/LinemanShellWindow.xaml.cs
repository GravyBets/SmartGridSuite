using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using SmartGridSuite.Client.Views.FieldTechnician.Panes;
using SmartGridSuite.Contracts.FieldTechnician;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Lineman
{
    public partial class LinemanShellWindow
    {
        private readonly ApiClient _connectivityApi =
            ClientAppSettings.CreateApiClient();

        private bool _navCollapsed;
        private bool _syncingNav;

        private const double NavExpandedWidth = 260;
        private const double NavCollapsedWidth = 58;

        private FieldTechTasksPaneView? _tasksPaneView;
        private FieldTechHistoryPaneView? _historyPaneView;

        /*
         * Lineman intentionally reuses the shared Site Dashboard engine.
         * The dashboard will be placed into restricted Lineman mode so only
         * Main, Site History, and Portal are exposed.
         */
        private SiteDashboardPaneView? _siteDashboardPaneView;

        public LinemanShellWindow()
        {
            InitializeComponent();

            ConnectivityService.StateChanged +=
                ConnectivityService_StateChanged;

            Closing +=
                LinemanShellWindow_Closing;

            Closed +=
                LinemanShellWindow_Closed;

            ApplyConnectivityState(
                ConnectivityService.CurrentState,
                ConnectivityService.CurrentMessage);

            _navCollapsed = true;

            ApplyNavState();

            // Default to Tasks.
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

        private static string? GetNavKey(
            ListBoxItem item)
        {
            return item.Tag?.ToString()
                   ?? item.ToolTip?.ToString()
                   ?? item.Content?.ToString();
        }

        private void NavList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
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

        private void ShowPane(
            ListBoxItem item)
        {
            switch (GetNavKey(item))
            {
                case "Site Lookup":
                    MainPaneHost.Content =
                        GetOrCreateSiteDashboardPane();
                    break;

                case "Tasks":
                    {
                        var tasksPane =
                            GetOrCreateTasksPane();

                        MainPaneHost.Content =
                            tasksPane;

                        _ = tasksPane.RefreshAsync();

                        break;
                    }

                case "History":
                    _historyPaneView ??=
                        new FieldTechHistoryPaneView();

                    MainPaneHost.Content =
                        _historyPaneView;
                    break;

                default:
                    MainPaneHost.Content =
                        GetOrCreateSiteDashboardPane();
                    break;
            }
        }

        private void ToggleNav_Click(
            object sender,
            RoutedEventArgs e)
        {
            _navCollapsed =
                !_navCollapsed;

            ApplyNavState();
        }

        private void ApplyNavState()
        {
            NavCol.Width =
                _navCollapsed
                    ? new GridLength(NavCollapsedWidth)
                    : new GridLength(NavExpandedWidth);

            NavShellBorder.Padding =
                _navCollapsed
                    ? new Thickness(5)
                    : new Thickness(12);

            NavListExpanded.Visibility =
                _navCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            NavListCollapsed.Visibility =
                _navCollapsed
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NavHeaderTextPanel.Visibility =
                _navCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            Grid.SetColumn(
                NavHeaderBtn,
                _navCollapsed ? 0 : 1);

            Grid.SetColumnSpan(
                NavHeaderBtn,
                _navCollapsed ? 2 : 1);

            NavHeaderBtn.HorizontalAlignment =
                _navCollapsed
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Right;

            NavSectionLabel.Visibility =
                _navCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            HomeButtonExpanded.Visibility =
                _navCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            HomeButtonCollapsed.Visibility =
                _navCollapsed
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (NavHeaderArrowPath.RenderTransform
                is RotateTransform rotate)
            {
                rotate.Angle =
                    _navCollapsed
                        ? 0
                        : 180;
            }

            NavHeaderBtn.ToolTip =
                _navCollapsed
                    ? "Expand navigation"
                    : "Collapse navigation";
        }

        private void HomeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private FieldTechTasksPaneView GetOrCreateTasksPane()
        {
            if (_tasksPaneView != null)
                return _tasksPaneView;

            _tasksPaneView =
                new FieldTechTasksPaneView();

            _tasksPaneView.OpenTicketRequested +=
                async ticket =>
                {
                    await OpenTicketsInDashboardAsync(
                        new[] { ticket });
                };

            _tasksPaneView.OpenAllTicketsRequested +=
                async tickets =>
                {
                    await OpenTicketsInDashboardAsync(
                        tickets);
                };

            return _tasksPaneView;
        }

        private async Task OpenTicketsInDashboardAsync(
            IEnumerable<FieldTechTicketListItemDto> tickets)
        {
            var cleanTickets =
                tickets
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

        private SiteDashboardPaneView
            GetOrCreateSiteDashboardPane()
        {
            if (_siteDashboardPaneView != null)
                return _siteDashboardPaneView;

            _siteDashboardPaneView = new SiteDashboardPaneView
            {
                CanManageSiteNotes = false,
                AccessMode = SiteDashboardAccessMode.Lineman
            };

            return _siteDashboardPaneView;
        }

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

        private async void RetryConnectivity_Click(
            object sender,
            RoutedEventArgs e)
        {
            ConnectivityService.BeginCheck();

            try
            {
                var result =
                    await _connectivityApi
                        .GetAsync<ApiHealthResponse>(
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
                // ApiClient already reports the offline state.
            }
            catch (ApiClient.ApiException ex)
            {
                ConnectivityService.ReportDegraded(
                    $"The health check returned server error {ex.StatusCode}.");
            }
        }

        private void LinemanShellWindow_Closed(
            object? sender,
            EventArgs e)
        {
            ConnectivityService.StateChanged -=
                ConnectivityService_StateChanged;
        }

        private void LinemanShellWindow_Closing(
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