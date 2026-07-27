using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.FieldTechnician;
using SmartGridSuite.Client.Views.Administration;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.ComponentModel;

namespace SmartGridSuite.Client.Views
{
    public partial class ModuleLauncherWindow : Window
    {
        public ModuleLauncherWindow()
        {
            ThemeService.ApplySavedTheme();

            InitializeComponent();

            ConnectivityService.StateChanged += ConnectivityService_StateChanged;

            Closing += ModuleLauncherWindow_Closing;

            Closed += ModuleLauncherWindow_Closed;

            ApplyConnectivityState(
                ConnectivityService.CurrentState,
                ConnectivityService.CurrentMessage);

            LoadThemeControl();
        }

        private readonly ApiClient _connectivityApi = ClientAppSettings.CreateApiClient();

        private readonly ClientVersionService _clientVersionService = ClientVersionService.Current;

        private const double NormalLauncherHeight = 580;
        private const double ConnectivityLauncherHeight = 640;

        private bool _isOpeningModule;
        private bool _loadingThemeControl;

        private readonly HashSet<Window> _trackedModuleWindows = new();

        private bool _isClosingApplication;

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
                if (!await CanOpenModulesForCurrentVersionAsync())
                    return;

                var existingWindow = Application.Current.Windows
                    .OfType<FieldTechnicianShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    TrackModuleWindow(existingWindow);
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                if (!await CurrentUserHasRequiredRoleAsync("TECHNICIAN", "Field Technician"))
                    return;

                existingWindow = Application.Current.Windows
                    .OfType<FieldTechnicianShellWindow>()
                    .FirstOrDefault();

                if (existingWindow != null)
                {
                    TrackModuleWindow(existingWindow);
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                var wnd = new FieldTechnicianShellWindow();

                OpenModuleWindow(wnd);
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
                if (!await CanOpenModulesForCurrentVersionAsync())
                    return;

                var existingWindow =
                    Application.Current.Windows
                        .OfType<DispatcherShellWindow>()
                        .FirstOrDefault();

                if (existingWindow != null)
                {
                    TrackModuleWindow(existingWindow);
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                if (!await CurrentUserHasRequiredRoleAsync(
                        "DISPATCH",
                        "Dispatcher"))
                {
                    return;
                }

                existingWindow =
                    Application.Current.Windows
                        .OfType<DispatcherShellWindow>()
                        .FirstOrDefault();

                if (existingWindow != null)
                {
                    TrackModuleWindow(existingWindow);
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                var wnd =
                    new DispatcherShellWindow();

                OpenModuleWindow(wnd);
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
                if (!await CanOpenModulesForCurrentVersionAsync())
                    return;

                var existingWindow =
                    Application.Current.Windows
                        .OfType<AdministrationShellWindow>()
                        .FirstOrDefault();

                if (existingWindow != null)
                {
                    TrackModuleWindow(existingWindow);
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                if (!await CurrentUserHasRequiredRoleAsync(
                        "ADMIN",
                        "Administration"))
                {
                    return;
                }

                existingWindow =
                    Application.Current.Windows
                        .OfType<AdministrationShellWindow>()
                        .FirstOrDefault();

                if (existingWindow != null)
                {
                    TrackModuleWindow(existingWindow);
                    BringExistingWindowForward(existingWindow);
                    Hide();
                    return;
                }

                var wnd =
                    new AdministrationShellWindow();

                OpenModuleWindow(wnd);
            }
            finally
            {
                EndModuleOpen();
            }
        }

        private async Task<bool> CanOpenModulesForCurrentVersionAsync()
        {
            var result =
                await _clientVersionService.CheckAsync();

            /*
             * Failure to check the version must never block field use.
             * Only a confirmed minimum-version violation blocks access.
             */
            if (result.State != ClientVersionState.Unsupported)
                return true;

            MessageBox.Show(
                $"This version of Smart Grid Suite is no longer supported.\n\n" +
                $"Installed version: {result.InstalledVersion}\n" +
                $"Minimum required: {result.MinimumSupportedVersion}\n\n" +
                "Close and reopen Smart Grid Suite to install the required update.",
                "Smart Grid Suite Update Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
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

        private void OpenModuleWindow(Window window)
        {
            TrackModuleWindow(window);

            window.Show();

            Hide();
        }

        private void TrackModuleWindow(Window window)
        {
            if (!_trackedModuleWindows.Add(window))
                return;

            window.Closed +=
                ModuleWindow_Closed;
        }

        private void ModuleWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -=
                    ModuleWindow_Closed;

                _trackedModuleWindows.Remove(window);
            }

            /*
             * Do not reopen the launcher while the application itself is shutting
             * down or while another tracked module remains open.
             */
            if (_isClosingApplication ||
                _trackedModuleWindows.Count > 0)
            {
                return;
            }

            /*
             * A module may close from code running during another UI event.
             * Queue the launcher restoration until that close operation finishes.
             */
            Dispatcher.BeginInvoke(
                new Action(ReturnToLauncher));
        }

        private void ReturnToLauncher()
        {
            if (_isClosingApplication ||
                !IsLoaded)
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            if (!IsVisible)
                Show();

            Activate();

            /*
             * Briefly raise the launcher in case the closing module left another
             * application above it.
             */
            Topmost = true;
            Topmost = false;

            Focus();
        }

        private void ModuleLauncherWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!e.Cancel)
                _isClosingApplication = true;
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

            ConnectivityBanner.Visibility =
                shouldShow
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ResizeForConnectivityBanner(shouldShow);
        }

        private void ResizeForConnectivityBanner(bool bannerIsVisible)
        {
            var targetHeight =
                bannerIsVisible
                    ? ConnectivityLauncherHeight
                    : NormalLauncherHeight;

            if (Math.Abs(Height - targetHeight) < 0.5)
                return;

            /*
             * Before the window is initially displayed, WindowStartupLocation handles
             * centering. After it is visible, adjust Top by half the height difference
             * so the launcher grows and shrinks around its vertical center.
             */
            if (IsLoaded && !double.IsNaN(Top))
            {
                var heightDifference = targetHeight - Height;
                Top -= heightDifference / 2;
            }

            Height = targetHeight;
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
            _isClosingApplication = true;

            ConnectivityService.StateChanged -=
                ConnectivityService_StateChanged;

            Closing -=
                ModuleLauncherWindow_Closing;

            foreach (var moduleWindow in
                     _trackedModuleWindows.ToList())
            {
                moduleWindow.Closed -=
                    ModuleWindow_Closed;
            }

            _trackedModuleWindows.Clear();
        }

        private sealed class ApiHealthResponse
        {
            public bool ApiAvailable { get; set; }

            public bool DatabaseAvailable { get; set; }

            public DateTimeOffset CheckedAtUtc { get; set; }
        }
    }
}