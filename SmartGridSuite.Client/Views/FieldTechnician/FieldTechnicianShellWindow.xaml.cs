using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using System.Windows;
using System.Windows.Controls;
using SmartGridSuite.Client.Views.FieldTechnician.Panes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Views.FieldTechnician
{
    public partial class FieldTechnicianShellWindow
    {
        private bool _navCollapsed;
        private bool _syncingNav;

        private FieldTechTasksPaneView? _tasksPaneView;
        private FieldTechHistoryPaneView? _historyPaneView;

        // Reuse the existing dashboard so all tab/session/pop-out logic stays shared.
        private SiteDashboardPaneView? _siteDashboardPaneView;

        public FieldTechnicianShellWindow()
        {
            InitializeComponent();

            UiScaleService.ApplyToWindow(this);

            // Default selection = Site Dashboard
            SelectNavIndex(0);
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

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            var home = new ModuleLauncherWindow();
            home.Show();
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

            await dashboard.OpenSitesFromFieldTechAsync(cleanSites);
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
    }
}