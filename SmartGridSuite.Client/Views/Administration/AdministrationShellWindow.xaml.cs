using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Administration.GeneralSettings;
using SmartGridSuite.Client.Views.Administration.SNMP;
using SmartGridSuite.Client.Views.Administration.Tickets;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class AdministrationShellWindow : Window
    {
        private readonly ApiClient _api;

        private readonly TechniciansAdminView _techniciansView;
        private readonly TrucksAdminView _trucksView;
        private readonly TicketsAdminView _ticketsView;
        private readonly GeneralSettingsAdminView _generalSettingsView;
        private readonly SnmpAdminView _snmpView;

        private bool _suppressNavSelectionChanged;
        private string? _currentNavTag;

        public AdministrationShellWindow()
        {
            InitializeComponent();

            _api = new ApiClient("https://localhost:7140");

            UiScaleService.ApplyToWindow(this);

            _techniciansView = new TechniciansAdminView(_api);
            _trucksView = new TrucksAdminView(_api);
            _ticketsView = new TicketsAdminView(_api);
            _generalSettingsView = new GeneralSettingsAdminView(_api);
            _snmpView = new SnmpAdminView(_api);

            NavigationListBox.SelectedIndex = 0;
        }

        private async void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressNavSelectionChanged)
                return;

            if (NavigationListBox.SelectedItem is not ListBoxItem item)
                return;

            var requestedTag = item.Tag as string;
            if (string.IsNullOrWhiteSpace(requestedTag))
                return;

            if (requestedTag == _currentNavTag)
                return;

            if (!await CanLeaveCurrentViewAsync())
            {
                RestoreCurrentSelection();
                return;
            }

            switch (requestedTag)
            {
                case "Technicians":
                    ShowTechnicians();
                    break;

                case "Trucks":
                    ShowTrucks();
                    break;

                case "Tickets":
                    ShowTickets();
                    break;

                case "GeneralSettings":
                    ShowGeneralSettings();
                    break;

                case "SNMP":
                    ShowSNMP();
                    break;

                default:
                    RestoreCurrentSelection();
                    return;
            }

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
            _suppressNavSelectionChanged = true;

            try
            {
                if (string.IsNullOrWhiteSpace(_currentNavTag))
                    return;

                var match = NavigationListBox.Items
                    .OfType<ListBoxItem>()
                    .FirstOrDefault(x => string.Equals(x.Tag as string, _currentNavTag, StringComparison.Ordinal));

                if (match is not null)
                    NavigationListBox.SelectedItem = match;
            }
            finally
            {
                _suppressNavSelectionChanged = false;
            }
        }

        private void ShowTechnicians()
        {
            ShowView(_techniciansView);
        }

        private void ShowTrucks()
        {
            ShowView(_trucksView);
        }

        private void ShowTickets()
        {
            ShowView(_ticketsView);
        }

        private void ShowGeneralSettings()
        {
            ShowView(_generalSettingsView);
        }

        private void ShowSNMP()
        {
            ShowView(_snmpView);
        }

        private void ShowView(UserControl view)
        {
            AdminContentHost.Content = view;
        }

        private async void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await CanLeaveCurrentViewAsync())
                return;

            var home = new ModuleLauncherWindow();
            home.Show();
            Close();
        }
    }
}