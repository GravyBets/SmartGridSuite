using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.FieldTechnician;
using SmartGridSuite.Client.Views.Administration;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views
{
    public partial class ModuleLauncherWindow : Window
    {
        public ModuleLauncherWindow()
        {
            ThemeService.ApplySavedTheme();

            InitializeComponent();

            ConnectivityService.StateChanged += ConnectivityService_StateChanged;

            Closed += ModuleLauncherWindow_Closed;

            ApplyConnectivityState(
                ConnectivityService.CurrentState,
                ConnectivityService.CurrentMessage);

            LoadThemeControl();
        }

        private readonly ApiClient _connectivityApi = ClientAppSettings.CreateApiClient();

        private bool _isOpeningModule;
        private bool _loadingThemeControl;

        private void LoadThemeControl()
        {
            _loadingThemeControl = true;

            try
            {
                ThemeComboBox.Items.Clear();

                foreach (var option in ThemeService.ThemeOptions)
                {
                    ThemeComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = option.DisplayName,
                        Tag = option.Theme
                    });
                }

                foreach (var item in ThemeComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (item.Tag is AppTheme theme &&
                        EqualityComparer<AppTheme>.Default.Equals(theme, ThemeService.Current))
                    {
                        ThemeComboBox.SelectedItem = item;
                        break;
                    }
                }

                if (ThemeComboBox.SelectedItem == null && ThemeComboBox.Items.Count > 0)
                    ThemeComboBox.SelectedIndex = 0;
            }
            finally
            {
                _loadingThemeControl = false;
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingThemeControl)
                return;

            if (ThemeComboBox.SelectedItem is not ComboBoxItem item ||
                item.Tag is not AppTheme theme)
            {
                return;
            }

            ThemeService.Apply(theme);

            _loadingThemeControl = true;

            try
            {
                foreach (var comboItem in ThemeComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (comboItem.Tag is AppTheme comboTheme &&
                        EqualityComparer<AppTheme>.Default.Equals(comboTheme, ThemeService.Current))
                    {
                        ThemeComboBox.SelectedItem = comboItem;
                        break;
                    }
                }
            }
            finally
            {
                _loadingThemeControl = false;
            }
        }

        private async void Tech_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginModuleOpen())
                return;

            try
            {
                var existingWindow = Application.Current.Windows
                    .OfType<FieldTechnicianShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    BringExistingWindowForward(existingWindow);
                    Close();
                    return;
                }

                if (!await CurrentUserHasRequiredRoleAsync("TECHNICIAN", "Field Technician"))
                    return;

                existingWindow = Application.Current.Windows
                    .OfType<FieldTechnicianShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    BringExistingWindowForward(existingWindow);
                    Close();
                    return;
                }

                var wnd = new FieldTechnicianShellWindow();
                wnd.Show();

                Close();
            }
            finally
            {
                EndModuleOpen();
            }
        }

        private async void Dispatch_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginModuleOpen())
                return;

            try
            {
                var existingWindow = Application.Current.Windows
                    .OfType<DispatcherShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    BringExistingWindowForward(existingWindow);
                    Close();
                    return;
                }

                if (!await CurrentUserHasRequiredRoleAsync("DISPATCH", "Dispatcher"))
                    return;

                /*
                 * Recheck after the role lookup. This protects against a second launcher
                 * window or another code path opening Dispatcher while access is loading.
                 */
                existingWindow = Application.Current.Windows
                    .OfType<DispatcherShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    BringExistingWindowForward(existingWindow);
                    Close();
                    return;
                }

                var wnd = new DispatcherShellWindow();
                wnd.Show();

                Close();
            }
            finally
            {
                EndModuleOpen();
            }
        }

        private async void Admin_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBeginModuleOpen())
                return;

            try
            {
                var existingWindow = Application.Current.Windows
                    .OfType<AdministrationShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                if (!await CurrentUserHasRequiredRoleAsync("ADMIN", "Administration"))
                    return;

                existingWindow = Application.Current.Windows
                    .OfType<AdministrationShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                var win = new AdministrationShellWindow
                {
                    Owner = this
                };

                win.Show();
                Hide();
            }
            finally
            {
                EndModuleOpen();
            }
        }

        private bool TryBeginModuleOpen()
        {
            if (_isOpeningModule)
                return false;

            _isOpeningModule = true;

            TechTile.IsEnabled = false;
            DispatchTile.IsEnabled = false;
            AdminTile.IsEnabled = false;

            Mouse.OverrideCursor = Cursors.Wait;

            return true;
        }

        private void EndModuleOpen()
        {
            _isOpeningModule = false;

            TechTile.IsEnabled = true;
            DispatchTile.IsEnabled = true;
            AdminTile.IsEnabled = true;

            Mouse.OverrideCursor = null;
        }

        private static void BringExistingWindowForward(Window window)
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            if (!window.IsVisible)
                window.Show();

            window.Activate();

            /*
             * This briefly raises an existing window above other windows and then
             * restores its normal behavior. It helps when the module is behind the launcher.
             */
            window.Topmost = true;
            window.Topmost = false;

            window.Focus();
        }

        private async Task<bool> CurrentUserHasRequiredRoleAsync(string requiredRoleCode, string moduleName)
        {
            try
            {
                var technician = await CurrentUserService.LoadCurrentTechnicianAsync(forceRefresh: true);

                if (technician == null)
                {
                    MessageBox.Show(
                        $"Access denied. Your Windows user '{CurrentUserService.CurrentEmployeeId}' was not found as an active technician.",
                        $"{moduleName} Access",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return false;
                }

                if (!CurrentUserService.HasRole(technician, requiredRoleCode))
                {
                    MessageBox.Show(
                        $"Access denied. Your Windows user '{CurrentUserService.CurrentEmployeeId}' does not have the {requiredRoleCode} role.",
                        $"{moduleName} Access",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return false;
                }

                return true;
            }

            catch (ApiClient.ApiConnectionException)
            {
                var cachedTechnician =
                    CurrentUserService.CurrentTechnician;

                /*
                 * A user already verified during this application session may continue
                 * into an authorized module while temporarily offline.
                 */
                if (cachedTechnician != null &&
                    CurrentUserService.HasRole(
                        cachedTechnician,
                        requiredRoleCode))
                {
                    ConnectivityService.ReportOffline(
                        "Offline — using the previously verified technician session.");

                    return true;
                }

                ConnectivityService.ReportOffline(
                    $"Unable to verify {moduleName} access while offline. " +
                    "Connect to the network and click Retry.");

                return false;
            }

            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to verify {moduleName} access.\n\n{ex.Message}",
                    $"{moduleName} Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void BugFeature_Click(object sender, RoutedEventArgs e)
        {
            var window = new BugFeatureRequestWindow
            {
                Owner = this
            };

            window.ShowDialog();
        }

        // Receives application-wide connection changes and safely updates this window
        // even when the originating API request completed on another thread.
        private void ConnectivityService_StateChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    ApplyConnectivityState(
                        e.State,
                        e.Message)));

                return;
            }

            ApplyConnectivityState(
                e.State,
                e.Message);
        }

        // Shows connection problems persistently without blocking the technician with
        // repeated modal windows.
        private void ApplyConnectivityState(ConnectivityState state, string message)
        {
            var shouldShow =
                state == ConnectivityState.Offline ||
                state == ConnectivityState.Degraded ||
                state == ConnectivityState.Checking;

            ConnectivityBanner.Visibility =
                shouldShow
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ConnectivityMessageText.Text =
                string.IsNullOrWhiteSpace(message)
                    ? "Unable to determine server availability."
                    : message;

            ConnectivityRetryButton.IsEnabled =
                state != ConnectivityState.Checking;

            ConnectivityRetryButton.Content =
                state == ConnectivityState.Checking
                    ? "Checking..."
                    : "Retry";
        }

        // Calls the lightweight health endpoint and restores normal UI state once both
        // the API and database are available again.
        private async void RetryConnectivity_Click(object sender, RoutedEventArgs e)
        {
            ConnectivityService.BeginCheck();

            try
            {
                var result =
                    await _connectivityApi.GetAsync<ApiHealthResponse>(
                        "api/health");

                if (result?.ApiAvailable == true &&
                    result.DatabaseAvailable)
                {
                    ConnectivityService.ReportOnline();
                    return;
                }

                ConnectivityService.ReportDegraded(
                    "The API is reachable, but the Smart Grid database is unavailable.");
            }
            catch (ApiClient.ApiConnectionException)
            {
                /*
                 * ApiClient already reported the offline state. No modal window is
                 * needed because the persistent banner displays the result.
                 */
            }
            catch (ApiClient.ApiException ex)
            {
                ConnectivityService.ReportDegraded(
                    $"The health check returned server error {ex.StatusCode}.");
            }
            catch (Exception ex)
            {
                /*
                 * Prevent an unexpected health-response or UI error from escaping this
                 * async event handler and terminating the WPF application.
                 */
                ConnectivityService.ReportDegraded(
                    $"Unable to complete the connection check: {ex.Message}");
            }
        }

        private void ModuleLauncherWindow_Closed(object? sender, EventArgs e)
        {
            ConnectivityService.StateChanged -=
                ConnectivityService_StateChanged;
        }

        private sealed class ApiHealthResponse
        {
            public bool ApiAvailable { get; set; }

            public bool DatabaseAvailable { get; set; }

            public DateTimeOffset CheckedAtUtc { get; set; }
        }
    }
}