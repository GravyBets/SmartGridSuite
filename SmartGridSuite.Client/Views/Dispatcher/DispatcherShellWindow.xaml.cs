using System.Windows;
using System.Windows.Controls;
using SmartGridSuite.Client.Views.Dispatcher.Panes;

namespace SmartGridSuite.Client.Views
{
    public partial class DispatcherShellWindow
    {
        private bool _navCollapsed;
        private bool _syncingNav;

        public DispatcherShellWindow()
        {
            InitializeComponent();

            // Keep current behavior aligned with nav order: 0 = Dashboard
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
                case "Dashboard":
                    MainPaneHost.Content = new DashboardPaneView();
                    break;

                case "Tasks":
                    MainPaneHost.Content = new TasksPaneView();
                    break;

                case "Tickets":
                    MainPaneHost.Content = new TicketsPaneView();
                    break;

                case "Technicians":
                    MainPaneHost.Content = new TechniciansPaneView();
                    break;

                default:
                    MainPaneHost.Content = new DashboardPaneView();
                    break;
            }
        }

        private void ToggleNav_Click(object sender, RoutedEventArgs e)
        {
            _navCollapsed = !_navCollapsed;

            NavCol.Width = _navCollapsed
                ? new GridLength(72)
                : new GridLength(260);

            NavListExpanded.Visibility = _navCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            NavListCollapsed.Visibility = _navCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

            NavHeaderBtn.Content = _navCollapsed
                ? "☰"
                : "☰  Navigation";
        }
    }
}