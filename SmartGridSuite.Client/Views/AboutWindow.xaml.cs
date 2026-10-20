using SmartGridSuite.Client.Services;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SmartGridSuite.Client.Views
{
    public partial class AboutWindow : Window
    {
        private readonly ClientVersionService _versionService =
            ClientVersionService.Current;

        private ClientVersionCheckResult? _lastVersionResult;
        private bool _checkingForUpdates;
        private bool _startingUpdate;

        public AboutWindow()
        {
            InitializeComponent();

            InterfaceScaleService.SetIsEnabled(
                this,
                false);

            VersionTextBlock.Text =
                $"Installed version " +
                $"{ClientVersionService.GetInstalledVersionText()}";

            ApplyCompanyLogo();

            ThemeService.ThemeChanged +=
                ThemeService_ThemeChanged;

            Loaded +=
                AboutWindow_Loaded;

            Closed +=
                AboutWindow_Closed;
        }

        private void ApplyCompanyLogo()
        {
            CompanyLogoImage.Source =
                new BitmapImage(
                    ThemeService.CurrentCompanyLogoUri);
        }

        private void ThemeService_ThemeChanged(
            object? sender,
            EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(ApplyCompanyLogo));

                return;
            }

            ApplyCompanyLogo();
        }

        private async void AboutWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshVersionStatusAsync();
        }

        private async void CheckForUpdates_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshVersionStatusAsync();
        }

        private async Task RefreshVersionStatusAsync()
        {
            if (_checkingForUpdates || _startingUpdate)
                return;

            _checkingForUpdates = true;
            _lastVersionResult = null;
            CheckForUpdatesButton.IsEnabled = false;
            UpdateNowButton.Visibility = Visibility.Collapsed;
            VersionStatusTextBlock.Text = "Checking for updates...";
            VersionDetailsTextBlock.Text = "";

            try
            {
                var result = await _versionService.CheckAsync(forceRefresh: true);
                if (!IsLoaded)
                    return;

                _lastVersionResult = result;

                var hasUpdate =
                    result.State is ClientVersionState.UpdateAvailable
                        or ClientVersionState.Unsupported;

                VersionStatusTextBlock.Text = hasUpdate
                    ? "A Smart Grid Suite update is available."
                    : result.Message;

                if (result.State == ClientVersionState.Unknown)
                {
                    VersionDetailsTextBlock.Text =
                        $"Installed: {result.InstalledVersion}. " +
                        "The application can continue running.";
                }
                else
                {
                    VersionDetailsTextBlock.Text =
                        $"Installed: {result.InstalledVersion}   •   " +
                        $"Latest: {result.LatestVersion}   •   " +
                        $"Minimum supported: {result.MinimumSupportedVersion}";
                }

                UpdateNowButton.Visibility =
                    hasUpdate ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _checkingForUpdates = false;
                if (IsLoaded)
                    CheckForUpdatesButton.IsEnabled = true;
            }
        }

        private async void UpdateNow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_startingUpdate || _checkingForUpdates ||
                _lastVersionResult?.State is not
                    (ClientVersionState.UpdateAvailable or ClientVersionState.Unsupported))
                return;

            _startingUpdate = true;
            UpdateNowButton.IsEnabled = false;

            try
            {
                // Recheck immediately before attempting to launch the installer.
                // An outdated cached response should never initiate an update.
                var result = await _versionService.CheckAsync(forceRefresh: true);
                if (!IsLoaded)
                    return;

                if (result.State is not
                    (ClientVersionState.UpdateAvailable or ClientVersionState.Unsupported))
                {
                    _lastVersionResult = result;
                    VersionStatusTextBlock.Text = result.Message;
                    UpdateNowButton.Visibility = Visibility.Collapsed;
                    return;
                }

                if (TryGetClickOnceManifestUri(result, out var deploymentUri))
                {
                    // .NET 8 has no ApplicationDeployment.Update() API.
                    // Re-activate the published ClickOnce deployment, which checks
                    // for the current published version before starting the app.
                    var start = new ProcessStartInfo("rundll32.exe")
                    {
                        UseShellExecute = false
                    };
                    start.ArgumentList.Add("dfshim.dll,ShOpenVerbApplication");
                    start.ArgumentList.Add(deploymentUri.AbsoluteUri);

                    using var updater = Process.Start(start)
                        ?? throw new InvalidOperationException(
                            "Windows could not start the ClickOnce updater.");

                    // Release our single-instance mutex before the updated app starts.
                    Application.Current.Shutdown();
                    return;
                }

                // A locally run/debug build has no ClickOnce deployment context.
                // Use the install page, not an invented .application filename.
                var installUrl = result.ServerVersion?.InstallUrl;
                if (!Uri.TryCreate(installUrl, UriKind.Absolute, out var installUri) ||
                    (installUri.Scheme != Uri.UriSchemeHttp &&
                     installUri.Scheme != Uri.UriSchemeHttps))
                {
                    MessageBox.Show(
                        "No valid installation location was provided by the server.",
                        "Smart Grid Suite Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo(installUri.AbsoluteUri)
                {
                    UseShellExecute = true
                });

                VersionStatusTextBlock.Text =
                    "Install page opened. Run the installer there, then restart Smart Grid Suite.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not start the update.\n\n{ex.Message}",
                    "Smart Grid Suite Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _startingUpdate = false;
                if (IsLoaded)
                    UpdateNowButton.IsEnabled = true;
            }
        }

        private static bool TryGetClickOnceManifestUri(
            ClientVersionCheckResult result,
            out Uri deploymentUri)
        {
            deploymentUri = null!;

            // Only trust the ClickOnce deployment environment for an installed
            // ClickOnce app; do not launch arbitrary locations returned by the API.
            var isClickOnce = string.Equals(
                Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            var updateLocation = isClickOnce
                ? Environment.GetEnvironmentVariable("ClickOnce_UpdateLocation")
                : null;

            // The API may explicitly provide a manifest URL in the future.
            // Today it provides a landing page instead.
            if (string.IsNullOrWhiteSpace(updateLocation))
                updateLocation = result.ServerVersion?.InstallUrl;

            if (!Uri.TryCreate(updateLocation, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !uri.AbsolutePath.EndsWith(
                    ".application", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            deploymentUri = uri;
            return true;
        }

        private void ChangeLogLink_Click(object sender, RoutedEventArgs e)
        {
            var window =
                new ChangeLogWindow
                {
                    Owner = this
                };

            window.ShowDialog();
        }

        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void AboutWindow_Closed(
            object? sender,
            EventArgs e)
        {
            ThemeService.ThemeChanged -=
                ThemeService_ThemeChanged;

            Loaded -=
                AboutWindow_Loaded;

            Closed -=
                AboutWindow_Closed;
        }
    }
}