using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.Trucks;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class TrucksAdminView : UserControl
    {
        private readonly TrucksApi _trucksApi;
        private readonly TruckStylesApi _truckStylesApi;

        private readonly ObservableCollection<TruckDto> _items = new();

        private bool _busy;
        private bool _isLoadedOnce;

        public TrucksAdminView(ApiClient api)
        {
            InitializeComponent();

            _trucksApi = new TrucksApi(api);
            _truckStylesApi = new TruckStylesApi(api);

            TrucksGrid.ItemsSource = _items;
            Loaded += TrucksAdminView_Loaded;
        }

        private async void TrucksAdminView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoadedOnce)
                return;

            _isLoadedOnce = true;
            await RefreshAsync();
        }

        private void SetStatus(string message) => StatusTextBlock.Text = message;

        private async Task RefreshAsync()
        {
            if (_busy) return;

            try
            {
                _busy = true;
                SetStatus("Loading trucks...");

                var trucks = await _trucksApi.GetTrucksAsync();

                _items.Clear();
                foreach (var truck in trucks.OrderBy(t => t.TruckNumber))
                {
                    _items.Add(truck);
                }

                SetStatus($"Loaded {_items.Count} truck(s).");
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(ex.Body ?? ex.Message, "Truck Load Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Load failed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Truck Load Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Load failed.");
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task OpenAddWindowAsync()
        {
            if (_busy) return;

            try
            {
                _busy = true;
                SetStatus("Loading truck styles...");

                var styles = await LoadStyleChoicesAsync();

                var window = new TruckEditWindow(_truckStylesApi, styles)
                {
                    Owner = Window.GetWindow(this)
                };

                _busy = false;

                var result = window.ShowDialog();
                if (result != true)
                {
                    SetStatus("Add truck canceled.");
                    return;
                }

                _busy = true;
                SetStatus("Creating truck...");

                var req = window.BuildCreateRequest();
                var created = await _trucksApi.CreateTruckAsync(req);

                await RefreshAsync();
                SetStatus("Truck created.");

                if (created != null)
                    SelectTruckById(created.Id);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(ex.Body ?? ex.Message, "Truck Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Save failed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Truck Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Save failed.");
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task OpenEditWindowAsync(TruckDto truck)
        {
            if (_busy) return;

            try
            {
                _busy = true;
                SetStatus("Loading truck styles...");

                var styles = await LoadStyleChoicesAsync();

                var window = new TruckEditWindow(_truckStylesApi, styles, truck)
                {
                    Owner = Window.GetWindow(this)
                };

                _busy = false;

                var result = window.ShowDialog();
                if (result != true)
                {
                    SetStatus("Edit canceled.");
                    return;
                }

                _busy = true;
                SetStatus("Saving truck...");

                var req = window.BuildUpdateRequest();
                await _trucksApi.UpdateTruckAsync(truck.Id, req);

                await RefreshAsync();
                SelectTruckById(truck.Id);
                SetStatus("Truck updated.");
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(ex.Body ?? ex.Message, "Truck Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Save failed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Truck Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Save failed.");
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task<System.Collections.Generic.List<TruckStyleDto>> LoadStyleChoicesAsync()
        {
            var styles = await _truckStylesApi.GetTruckStylesAsync(includeInactive: false);
            return styles
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .ToList();
        }

        private void SelectTruckById(int id)
        {
            var match = _items.FirstOrDefault(t => t.Id == id);
            if (match != null)
            {
                TrucksGrid.SelectedItem = match;
                TrucksGrid.ScrollIntoView(match);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
            => await RefreshAsync();

        private async void AddTruck_Click(object sender, RoutedEventArgs e)
            => await OpenAddWindowAsync();

        private async void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (TrucksGrid.SelectedItem is not TruckDto truck)
            {
                SetStatus("Select a truck first.");
                return;
            }

            await OpenEditWindowAsync(truck);
        }
        
    }
}