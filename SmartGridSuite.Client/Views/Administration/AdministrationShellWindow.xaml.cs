using System.Windows;
using System.Windows.Controls;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Administration.Tickets;
using SmartGridSuite.Client.Views.Administration.SNMP;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class AdministrationShellWindow : Window
    {
        private readonly ApiClient _api;

        private readonly TechniciansAdminView _techniciansView;
        private readonly TrucksAdminView _trucksView;
        private readonly TicketsAdminView _ticketsView;
        private readonly SnmpAdminView _snmpView;

        public AdministrationShellWindow()
        {
            InitializeComponent();

            _api = new ApiClient("https://localhost:7140");

            _techniciansView = new TechniciansAdminView(_api);
            _trucksView = new TrucksAdminView(_api);
            _ticketsView = new TicketsAdminView(_api);
            _snmpView = new SnmpAdminView(_api);

            NavigationListBox.SelectedIndex = 0;
        }

        private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavigationListBox.SelectedItem is not ListBoxItem item)
                return;

            switch (item.Tag as string)
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

                case "SNMP":
                    ShowSNMP();
                    break;
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

        private void ShowSNMP()
        {
            ShowView(_snmpView);
        }

        private void ShowView(UserControl view)
        {
            AdminContentHost.Content = view;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            var home = new ModuleLauncherWindow();
            home.Show();
            Close();
        }
    }
}