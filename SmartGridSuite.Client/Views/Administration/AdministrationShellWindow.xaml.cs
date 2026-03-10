using System.Windows;
using System.Windows.Controls;
using SmartGridSuite.Client.Services;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class AdministrationShellWindow : Window
    {
        private readonly ApiClient _api;

        private readonly TechniciansAdminView _techniciansView;
        private readonly TrucksAdminView _trucksView;

        public AdministrationShellWindow()
        {
            InitializeComponent();

            _api = new ApiClient("https://localhost:7140");

            _techniciansView = new TechniciansAdminView(_api);
            _trucksView = new TrucksAdminView(_api);

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

        private void ShowView(UserControl view)
        {
            AdminContentHost.Content = view;
        }
    }
}