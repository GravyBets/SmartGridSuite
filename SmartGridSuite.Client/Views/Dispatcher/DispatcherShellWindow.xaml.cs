using System.Windows;
using System.Windows.Controls;
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
        private DailyAssignmentsPaneView? _dailyAssignmentsPaneView; 
        private int _currentNavIndex;
        private bool _allowCloseWithoutPrompt;
        private bool _closePromptRunning;

        private readonly ApiClient _api = new("https://localhost:7140");

        public DispatcherShellWindow()
        {
            InitializeComponent();

            UiScaleService.ApplyToWindow(this);

            Closing += DispatcherShellWindow_Closing;

            // Default selection = Site Dashboard
            SelectNavIndex(0);
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
                    MainPaneHost.Content = new TaskPaneView();
                    break;

                case "Tickets":
                    MainPaneHost.Content = new TicketsPaneView();
                    break;

                case "Technicians":
                    MainPaneHost.Content = new TechniciansPaneView();
                    break;

                case "Daily Assignments":
                    _dailyAssignmentsPaneView ??= new DailyAssignmentsPaneView();
                    MainPaneHost.Content = _dailyAssignmentsPaneView;
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

            NavCol.Width = _navCollapsed
                ? new GridLength(88)
                : new GridLength(280);

            NavListExpanded.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            NavListCollapsed.Visibility = _navCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

            NavSectionLabel.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            HomeButtonExpanded.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            HomeButtonCollapsed.Visibility = _navCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            var canLeave = await ConfirmCurrentPaneCanCloseAsync();

            if (!canLeave)
                return;

            _allowCloseWithoutPrompt = true;

            var home = new ModuleLauncherWindow();
            home.Show();

            Close();
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