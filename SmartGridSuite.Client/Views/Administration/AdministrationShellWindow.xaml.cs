using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Administration.GeneralSettings;
using SmartGridSuite.Client.Views.Administration.SNMP;
using SmartGridSuite.Client.Views.Administration.Tickets;
using SmartGridSuite.Client.Views.Administration.WriteUpWorkflow;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class AdministrationShellWindow : Window
    {
        private readonly ApiClient _api;

        private readonly TechniciansAdminView _techniciansView;
        private readonly TrucksAdminView _trucksView;
        private readonly TicketsAdminView _ticketsView;
        private readonly GeneralSettingsAdminView _generalSettingsView;
        private readonly WriteUpWorkflowAdminView _writeUpWorkflowView;
        private readonly SnmpAdminView _snmpView;

        private bool _navCollapsed;
        private bool _syncingNav;
        private int _currentNavIndex;
        private string? _currentNavTag;

        private const double NavExpandedWidth = 260;
        private const double NavCollapsedWidth = 58;

        public AdministrationShellWindow()
        {
            InitializeComponent();

            _api = ClientAppSettings.CreateApiClient();

            _techniciansView = new TechniciansAdminView(_api);
            _trucksView = new TrucksAdminView(_api);
            _ticketsView = new TicketsAdminView(_api);
            _generalSettingsView = new GeneralSettingsAdminView(_api);
            _writeUpWorkflowView = new WriteUpWorkflowAdminView(_api);
            _snmpView = new SnmpAdminView(_api);

            _navCollapsed = true;
            ApplyNavState();

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
                _currentNavIndex = index;
                _currentNavTag = GetNavKey(item);
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
            var requestedTag = GetNavKey(item);

            if (string.IsNullOrWhiteSpace(requestedTag))
                return;

            if (requestedTag == _currentNavTag)
                return;

            if (!await CanLeaveCurrentViewAsync())
            {
                RestoreCurrentSelection();
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
            _currentNavTag = requestedTag;
        }

        private async Task<bool> CanLeaveCurrentViewAsync()
        {
            if (_currentNavTag == "SNMP")
                return await _snmpView.ConfirmPendingChangesAsync();

            return true;
        }

        private void RestoreCurrentSelection()
        {
            _syncingNav = true;

            try
            {
                NavListExpanded.SelectedIndex = _currentNavIndex;
                NavListCollapsed.SelectedIndex = _currentNavIndex;
            }
            finally
            {
                _syncingNav = false;
            }
        }

        private void ShowPane(ListBoxItem item)
        {
            switch (GetNavKey(item))
            {
                case "Technicians":
                    ShowView(_techniciansView);
                    break;

                case "Trucks":
                    ShowView(_trucksView);
                    break;

                case "Tickets":
                    ShowView(_ticketsView);
                    break;

                case "WriteUpWorkflow":
                    ShowView(_writeUpWorkflowView);
                    break;

                case "GeneralSettings":
                    ShowView(_generalSettingsView);
                    break;

                case "SNMP":
                    ShowView(_snmpView);
                    break;

                default:
                    ShowView(_techniciansView);
                    break;
            }
        }

        private void ShowView(UserControl view)
        {
            AdminContentHost.Content = view;
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
    }
}