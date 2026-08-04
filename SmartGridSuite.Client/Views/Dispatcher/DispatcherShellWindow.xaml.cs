using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using System.ComponentModel;
using SmartGridSuite.Client.Services;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views
{
    public partial class DispatcherShellWindow
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

        public DispatcherShellWindow()
        {
            InitializeComponent();

            Closing += DispatcherShellWindow_Closing;
            Closed += DispatcherShellWindow_Closed;

            _navCollapsed = true;
            ApplyNavState();

            SelectNavIndex(1);
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

            if (NavHeaderArrowPath.RenderTransform is RotateTransform rotate)
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

        private void DispatcherShellWindow_Closed(
            object? sender,
            EventArgs e)
        {
            Closing -= DispatcherShellWindow_Closing;
            Closed -= DispatcherShellWindow_Closed;

            _siteDashboardPaneView?.Shutdown();

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