using SmartGridSuite.Contracts.Administration.Technicians;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class AssignTechnicianToTruckWindow : Window
    {
        private ICollectionView? _view;

        public TechnicianDto? SelectedTechnician { get; private set; }

        public AssignTechnicianToTruckWindow(IEnumerable<TechnicianDto> availableTechnicians, string truckNumber)
        {
            InitializeComponent();

            Title = $"Add Technician to Truck {truckNumber}";
            HeaderTextBlock.Text = $"Truck {truckNumber}";

            var items = availableTechnicians
                .OrderByDescending(x => x.IsOnShift)
                .ThenBy(x => x.Name)
                .ToList();

            TechniciansList.ItemsSource = items;

            _view = CollectionViewSource.GetDefaultView(TechniciansList.ItemsSource);
            _view.Filter = FilterTechnician;
            _view.Refresh();

            Loaded += (_, _) => SearchBox.Focus();
        }

        private bool FilterTechnician(object obj)
        {
            if (obj is not TechnicianDto tech)
                return false;

            if (OnDutyOnlyCheckBox.IsChecked == true && !tech.IsOnShift)
                return false;

            var q = (SearchBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(q))
                return true;

            bool Match(string? value) =>
                !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0;

            return Match(tech.Name) || Match(tech.EmployeeId);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _view?.Refresh();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _view?.Refresh();
        }

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            if (TechniciansList.SelectedItem is not TechnicianDto tech)
            {
                MessageBox.Show(
                    "Select a technician first.",
                    "Add Technician",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SelectedTechnician = tech;
            DialogResult = true;
        }

        private void TechniciansList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TechniciansList.SelectedItem is TechnicianDto)
                Assign_Click(sender, e);
        }
    }
}