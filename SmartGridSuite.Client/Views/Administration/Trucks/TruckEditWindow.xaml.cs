using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.Trucks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class TruckEditWindow : Window
    {
        private readonly TruckStylesApi _truckStylesApi;
        private readonly List<TruckStyleDto> _styles = new();
        private const int TruckNumberMaxLength = 20;

        public TruckEditWindow(
            TruckStylesApi truckStylesApi,
            IEnumerable<TruckStyleDto> styles,
            TruckDto? truck = null)
        {
            InitializeComponent();

            _truckStylesApi = truckStylesApi;
            LoadStyles(styles);

            if (truck != null)
                LoadTruck(truck);
            else
                InServiceComboBox.SelectedIndex = 0;
        }

        public CreateTruckRequest BuildCreateRequest()
        {
            return new CreateTruckRequest
            {
                TruckNumber = TruckNumberTextBox.Text.Trim(),
                TruckStyleId = GetSelectedStyleId(),
                IsActive = GetSelectedInService()
            };
        }

        public UpdateTruckRequest BuildUpdateRequest()
        {
            return new UpdateTruckRequest
            {
                TruckNumber = TruckNumberTextBox.Text.Trim(),
                TruckStyleId = GetSelectedStyleId(),
                IsActive = GetSelectedInService()
            };
        }

        private void LoadStyles(IEnumerable<TruckStyleDto> styles)
        {
            _styles.Clear();
            _styles.AddRange(styles.OrderBy(x => x.Name));

            TruckStyleComboBox.ItemsSource = null;
            TruckStyleComboBox.ItemsSource = _styles;
        }

        private void LoadTruck(TruckDto truck)
        {
            TruckNumberTextBox.Text = truck.TruckNumber;

            if (truck.TruckStyleId.HasValue)
                TruckStyleComboBox.SelectedValue = truck.TruckStyleId.Value;

            InServiceComboBox.SelectedIndex = truck.IsActive ? 0 : 1;
        }

        private int? GetSelectedStyleId()
        {
            return TruckStyleComboBox.SelectedValue is int id ? id : null;
        }

        private bool GetSelectedInService()
        {
            return (InServiceComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() == "Yes";
        }

        private async void AddStyle_Click(object sender, RoutedEventArgs e)
        {
            var window = new TruckStyleEditWindow
            {
                Owner = this
            };

            var result = window.ShowDialog();
            if (result != true)
                return;

            try
            {
                var created = await _truckStylesApi.CreateTruckStyleAsync(new CreateTruckStyleRequest
                {
                    Name = window.StyleName,
                    IsActive = true
                });

                if (created == null)
                    return;

                _styles.Add(created);
                var ordered = _styles.OrderBy(x => x.Name).ToList();

                TruckStyleComboBox.ItemsSource = null;
                TruckStyleComboBox.ItemsSource = ordered;

                _styles.Clear();
                _styles.AddRange(ordered);

                TruckStyleComboBox.SelectedValue = created.Id;
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(ex.Body ?? ex.Message, "Add Style Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, "Add Style Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var truckNumber = TruckNumberTextBox.Text.Trim();

            if (truckNumber.Length == 0)
            {
                MessageBox.Show("Truck # is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (truckNumber.Length > TruckNumberMaxLength)
            {
                MessageBox.Show(
                    $"Truck # must be {TruckNumberMaxLength} characters or less.\n\nCurrent length: {truckNumber.Length}",
                    "Truck # Too Long",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (GetSelectedStyleId() == null)
            {
                MessageBox.Show("Select a truck style.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}