#nullable enable
using SmartGridSuite.Client.Models.Administration;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.Trucks;
using SmartGridSuite.Contracts.Administration.Technicians;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Administration
{
    public partial class TechniciansAdminView : UserControl
    {
        private readonly TechniciansApi _techniciansApi;
        private readonly TrucksApi _trucksApi;
        private bool _busy;
        private bool _loadedOnce;

        public ObservableCollection<AdminTechnicianRow> Items { get; } = new();

        public TechniciansAdminView(ApiClient api)
        {
            InitializeComponent();

            AdminTechGrid.ItemsSource = Items;
            _techniciansApi = new TechniciansApi(api);
            _trucksApi = new TrucksApi(api);

            Loaded += TechniciansAdminView_Loaded;
        }

        private async void TechniciansAdminView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loadedOnce)
                return;

            _loadedOnce = true;
            await RefreshAsync();
        }

        private void SetStatus(string msg) => StatusText.Text = msg;

        private async Task RefreshAsync()
        {
            if (_busy) return;

            try
            {
                _busy = true;
                SetStatus("Loading technicians...");

                var techs = await _techniciansApi.GetTechniciansAsync(includeInactive: true);

                Items.Clear();
                foreach (var t in techs.OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
                {
                    Items.Add(new AdminTechnicianRow
                    {
                        Id = t.Id,
                        EmployeeId = t.EmployeeId,
                        FirstName = t.FirstName,
                        LastName = t.LastName,
                        Title = t.Title,
                        IsActive = t.IsActive,
                        HomeTruckId = t.HomeTruckId,
                        HomeTruckNumber = t.HomeTruckNumber,
                        HomeTruckDisplayName = t.HomeTruckDisplayName,
                        WorksMonday = t.WorksMonday,
                        WorksTuesday = t.WorksTuesday,
                        WorksWednesday = t.WorksWednesday,
                        WorksThursday = t.WorksThursday,
                        WorksFriday = t.WorksFriday,
                        WorksSaturday = t.WorksSaturday,
                        WorksSunday = t.WorksSunday,
                        RoleCodes = t.RoleCodes?.ToList() ?? new List<string>()
                    });
                }

                SetStatus($"Loaded {Items.Count} technicians.");
            }
            catch (ApiClient.ApiException ex)
            {
                SetStatus("Error: " + (ex.Body ?? ex.Message));
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
            => await RefreshAsync();

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            await OpenAddWindowAsync();
        }

        private async void EditSelected_Click(object sender, RoutedEventArgs e)
        {
            if (AdminTechGrid.SelectedItem is not AdminTechnicianRow row)
            {
                SetStatus("Select a technician first.");
                return;
            }

            await OpenEditWindowAsync(row);
        }

        private async void EditRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not AdminTechnicianRow row)
                return;

            await OpenEditWindowAsync(row);
        }

        

        private async Task OpenAddWindowAsync()
        {
            if (_busy) return;

            try
            {
                _busy = true;
                SetStatus("Loading trucks...");

                var trucks = await LoadTruckChoicesAsync();

                var window = new TechnicianEditWindow(trucks)
                {
                    Owner = Window.GetWindow(this)
                };

                _busy = false;

                var result = window.ShowDialog();
                if (result != true)
                {
                    SetStatus("Add technician canceled.");
                    return;
                }

                _busy = true;
                SetStatus("Creating technician...");

                var req = window.BuildCreateRequest();
                await _techniciansApi.CreateTechnicianAsync(req);

                await RefreshAsync();
                SetStatus("Technician created.");
            }
            catch (ApiClient.ApiException ex)
            {
                SetStatus("Error: " + (ex.Body ?? ex.Message));
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task OpenEditWindowAsync(AdminTechnicianRow row)
        {
            if (_busy) return;

            try
            {
                _busy = true;
                SetStatus("Loading trucks...");

                var trucks = await LoadTruckChoicesAsync();

                var window = new TechnicianEditWindow(trucks, row)
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
                SetStatus("Saving technician...");

                var req = window.BuildUpdateRequest();
                await _techniciansApi.UpdateTechnicianAsync(row.Id, req);

                await RefreshAsync();
                SetStatus("Technician updated.");
            }
            catch (ApiClient.ApiException ex)
            {
                SetStatus("Error: " + (ex.Body ?? ex.Message));
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task<List<TruckDto>> LoadTruckChoicesAsync()
        {
            var trucks = await _trucksApi.GetTrucksAsync();
            return trucks
                .Where(t => t.IsActive)
                .OrderBy(t => t.TruckNumber)
                .ToList();
        }
    }
}