#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.FieldTechnician;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;

namespace SmartGridSuite.Client.Views.FieldTechnician.Panes
{
    public partial class FieldTechTasksPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? OpenSiteRequested;
        public event EventHandler<IReadOnlyList<string>>? OpenAllSitesRequested;

        private readonly HttpClient _http = new()
        {
            BaseAddress = new Uri("https://localhost:7140/")
        };

        private bool _loadedOnce;
        private bool _busyLoading;
        private FieldTechTicketListItemDto? _selectedTicket;
        private string _statusMessage = "Ready.";

        public ObservableCollection<FieldTechTicketListItemDto> Tickets { get; } = new();

        public FieldTechTicketListItemDto? SelectedTicket
        {
            get => _selectedTicket;
            set
            {
                if (_selectedTicket == value)
                    return;

                _selectedTicket = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public FieldTechTasksPaneView()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += async (_, __) =>
            {
                if (_loadedOnce)
                    return;

                _loadedOnce = true;
                await LoadTasksAsync();
            };
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadTasksAsync();
        }

        private async Task LoadTasksAsync()
        {
            if (_busyLoading)
                return;

            try
            {
                _busyLoading = true;
                StatusMessage = "Loading assigned tasks...";

                var technician = await CurrentUserService.LoadCurrentTechnicianAsync(forceRefresh: true);

                if (technician == null || string.IsNullOrWhiteSpace(technician.EmployeeId))
                {
                    Tickets.Clear();
                    StatusMessage = "No active technician record was found for the signed-in user.";
                    return;
                }

                var employeeId = Uri.EscapeDataString(technician.EmployeeId);

                var rows = await _http.GetFromJsonAsync<List<FieldTechTicketListItemDto>>(
                    $"api/tickets/field-tech/tasks/{employeeId}");

                Tickets.Clear();

                foreach (var row in rows ?? new List<FieldTechTicketListItemDto>())
                    Tickets.Add(row);

                StatusMessage = $"Loaded {Tickets.Count} assigned task(s) for {technician.Name}.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading assigned tasks.";
                MessageBox.Show(
                    ex.Message,
                    "Field Technician Tasks",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _busyLoading = false;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void OpenAll_Click(object sender, RoutedEventArgs e)
        {
            var sites = Tickets
                .Select(x => (x.Site ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sites.Count == 0)
            {
                MessageBox.Show(
                    "There are no task sites to open.",
                    "Open All",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            OpenAllSitesRequested?.Invoke(this, sites);
        }

        private void TasksGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SelectedTicket == null)
                return;

            var site = (SelectedTicket.Site ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(site))
                return;

            OpenSiteRequested?.Invoke(this, site);
        }
    }
}