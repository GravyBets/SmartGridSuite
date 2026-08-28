#nullable enable

using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.ConnectedClients;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.Administration.ConnectedClients
{
    public partial class ConnectedClientsAdminView :
        UserControl,
        INotifyPropertyChanged
    {
        private readonly ApiClient _api;

        private readonly DispatcherTimer _refreshTimer;

        private bool _isLoading;
        private bool _updatingFilters;

        public event PropertyChangedEventHandler?
            PropertyChanged;

        public ObservableCollection<ConnectedClientRowViewModel>
            Clients
        { get; } = new();

        public ICollectionView ClientsView { get; }

        private int _onlineClientCount;

        public int OnlineClientCount
        {
            get => _onlineClientCount;
            private set
            {
                if (_onlineClientCount == value)
                    return;

                _onlineClientCount = value;
                OnPropertyChanged();
            }
        }

        private int _outdatedClientCount;

        public int OutdatedClientCount
        {
            get => _outdatedClientCount;
            private set
            {
                if (_outdatedClientCount == value)
                    return;

                _outdatedClientCount = value;
                OnPropertyChanged();
            }
        }

        private int _versionsInUseCount;

        public int VersionsInUseCount
        {
            get => _versionsInUseCount;
            private set
            {
                if (_versionsInUseCount == value)
                    return;

                _versionsInUseCount = value;
                OnPropertyChanged();
            }
        }

        private string _latestVersionText =
            "Latest version: —";

        public string LatestVersionText
        {
            get => _latestVersionText;
            private set
            {
                if (_latestVersionText == value)
                    return;

                _latestVersionText = value;
                OnPropertyChanged();
            }
        }

        private string _statusMessage =
            "Ready.";

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

        public ConnectedClientsAdminView(
            ApiClient api)
        {
            InitializeComponent();

            _api = api;

            DataContext = this;

            ClientsView =
                CollectionViewSource
                    .GetDefaultView(Clients);

            ClientsView.Filter =
                FilterClient;

            ClientsGrid.ItemsSource =
                ClientsView;

            StateFilterComboBox.ItemsSource =
                new[]
                {
                    "All",
                    "Online",
                    "Offline"
                };

            StateFilterComboBox.SelectedIndex = 0;

            ModuleFilterComboBox.ItemsSource =
                new[] { "All" };

            ModuleFilterComboBox.SelectedIndex = 0;

            VersionFilterComboBox.ItemsSource =
                new[] { "All" };

            VersionFilterComboBox.SelectedIndex = 0;

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(30)
                };

            _refreshTimer.Tick +=
                RefreshTimer_Tick;

            Loaded +=
                ConnectedClientsAdminView_Loaded;

            Unloaded +=
                ConnectedClientsAdminView_Unloaded;
        }

        private async void
            ConnectedClientsAdminView_Loaded(
                object sender,
                RoutedEventArgs e)
        {
            _refreshTimer.Start();

            await RefreshAsync();
        }

        private void
            ConnectedClientsAdminView_Unloaded(
                object sender,
                RoutedEventArgs e)
        {
            _refreshTimer.Stop();
        }

        private async void RefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await RefreshAsync();
        }

        private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_isLoading)
                return;

            try
            {
                _isLoading = true;

                RefreshButton.IsEnabled = false;

                StatusMessage =
                    "Refreshing connected clients...";

                var response =
                    await _api.GetAsync<
                        ConnectedClientsResponse>(
                        "api/client-presence");

                if (response == null)
                {
                    StatusMessage =
                        "The server returned no client presence data.";

                    return;
                }

                var selectedModule =
                    ModuleFilterComboBox
                        .SelectedItem?
                        .ToString()
                    ?? "All";

                var selectedVersion =
                    VersionFilterComboBox
                        .SelectedItem?
                        .ToString()
                    ?? "All";

                Clients.Clear();

                foreach (var client in response.Clients)
                {
                    Clients.Add(
                        new ConnectedClientRowViewModel(
                            client));
                }

                OnlineClientCount =
                    response.OnlineClientCount;

                OutdatedClientCount =
                    response.OutdatedClientCount;

                VersionsInUseCount =
                    response.VersionsInUseCount;

                LatestVersionText =
                    string.IsNullOrWhiteSpace(
                        response.LatestVersion)
                        ? "Latest version: —"
                        : $"Latest version: {response.LatestVersion}";

                RebuildFilterOptions(
                    selectedModule,
                    selectedVersion);

                ClientsView.Refresh();

                StatusMessage =
                    $"Last refreshed " +
                    $"{DateTime.Now:h:mm:ss tt} • " +
                    $"{Clients.Count} known client(s)";
            }
            catch (ApiClient.ApiConnectionException)
            {
                StatusMessage =
                    "Offline — unable to refresh connected clients.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusMessage =
                    $"Unable to refresh connected clients. " +
                    $"Server error {ex.StatusCode}.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    $"Unable to refresh connected clients: " +
                    ex.Message;
            }
            finally
            {
                _isLoading = false;

                RefreshButton.IsEnabled = true;
            }
        }

        private void RebuildFilterOptions(
            string selectedModule,
            string selectedVersion)
        {
            _updatingFilters = true;

            try
            {
                var modules =
                    Clients
                        .Select(x => x.CurrentModule)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

                var moduleOptions =
                    new List<string>
                    {
                        "All"
                    };

                moduleOptions.AddRange(modules);

                ModuleFilterComboBox.ItemsSource =
                    moduleOptions;

                ModuleFilterComboBox.SelectedItem =
                    moduleOptions.Any(x =>
                        string.Equals(
                            x,
                            selectedModule,
                            StringComparison.OrdinalIgnoreCase))
                        ? selectedModule
                        : "All";

                var versions =
                    Clients
                        .Select(x => x.ClientVersion)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(
                            ParseVersionForSort)
                        .ThenByDescending(x => x)
                        .ToList();

                var versionOptions =
                    new List<string>
                    {
                        "All"
                    };

                versionOptions.AddRange(versions);

                VersionFilterComboBox.ItemsSource =
                    versionOptions;

                VersionFilterComboBox.SelectedItem =
                    versionOptions.Any(x =>
                        string.Equals(
                            x,
                            selectedVersion,
                            StringComparison.OrdinalIgnoreCase))
                        ? selectedVersion
                        : "All";
            }
            finally
            {
                _updatingFilters = false;
            }
        }

        private static Version ParseVersionForSort(
            string? versionText)
        {
            return Version.TryParse(
                versionText,
                out var version)
                    ? version
                    : new Version(0, 0);
        }

        private void Filter_Changed(
            object sender,
            EventArgs e)
        {
            if (_updatingFilters)
                return;

            ClientsView?.Refresh();
        }

        private bool FilterClient(
            object item)
        {
            if (item is not
                ConnectedClientRowViewModel client)
            {
                return false;
            }

            var search =
                (SearchTextBox?.Text ?? string.Empty)
                    .Trim();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var matchesSearch =
                    Contains(
                        client.DisplayName,
                        search) ||
                    Contains(
                        client.EmployeeId,
                        search) ||
                    Contains(
                        client.WindowsUser,
                        search) ||
                    Contains(
                        client.MachineName,
                        search) ||
                    Contains(
                        client.CurrentModule,
                        search) ||
                    Contains(
                        client.ClientVersion,
                        search);

                if (!matchesSearch)
                    return false;
            }

            var module =
                ModuleFilterComboBox?
                    .SelectedItem?
                    .ToString()
                ?? "All";

            if (!module.Equals(
                    "All",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    client.CurrentModule,
                    module,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var version =
                VersionFilterComboBox?
                    .SelectedItem?
                    .ToString()
                ?? "All";

            if (!version.Equals(
                    "All",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    client.ClientVersion,
                    version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var state =
                StateFilterComboBox?
                    .SelectedItem?
                    .ToString()
                ?? "All";

            if (state.Equals(
                    "Online",
                    StringComparison.OrdinalIgnoreCase) &&
                !client.IsOnline)
            {
                return false;
            }

            if (state.Equals(
                    "Offline",
                    StringComparison.OrdinalIgnoreCase) &&
                client.IsOnline)
            {
                return false;
            }

            return true;
        }

        private static bool Contains(
            string? value,
            string search)
        {
            return
                (value ?? string.Empty)
                .Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void OnPropertyChanged(
            [CallerMemberName]
            string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }
    }

    public sealed class ConnectedClientRowViewModel
    {
        public long Id { get; }

        public string EmployeeId { get; }

        public string DisplayName { get; }

        public string WindowsUser { get; }

        public string MachineName { get; }

        public string ClientVersion { get; }

        public string CurrentModule { get; }

        public bool IsOnline { get; }

        public bool IsOutdated { get; }

        public string StateText =>
            IsOnline
                ? "Online"
                : "Offline";

        public string VersionStateText =>
            IsOutdated
                ? "Outdated"
                : "Current";

        public string LastSeenText { get; }

        public ConnectedClientRowViewModel(
            ConnectedClientDto dto)
        {
            Id = dto.Id;

            EmployeeId =
                dto.EmployeeId ?? string.Empty;

            DisplayName =
                string.IsNullOrWhiteSpace(
                    dto.DisplayName)
                    ? "(Unknown User)"
                    : dto.DisplayName;

            WindowsUser =
                dto.WindowsUser ?? string.Empty;

            MachineName =
                dto.MachineName ?? string.Empty;

            ClientVersion =
                dto.ClientVersion ?? string.Empty;

            CurrentModule =
                dto.CurrentModule ?? string.Empty;

            IsOnline =
                dto.IsOnline;

            IsOutdated =
                dto.IsOutdated;

            /*
             * MySQL DateTime values return as DateTimeKind.Unspecified.
             * The API contract says these values are UTC, so explicitly
             * mark them UTC before converting them for display.
             */
            var lastSeenUtc =
                DateTime.SpecifyKind(
                    dto.LastSeenAtUtc,
                    DateTimeKind.Utc);

            LastSeenText =
                lastSeenUtc
                    .ToLocalTime()
                    .ToString(
                        "M/d/yyyy h:mm:ss tt");
        }
    }
}