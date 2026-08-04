using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private bool _portalInitialized;
        private bool _portalDisposed;

        private string _portalUrl = string.Empty;
        private string _lastPortalRequestedUrl = string.Empty;

        public async Task EnsurePortalReadyAsync()
        {
            if (_portalDisposed)
                return;

            if (_portalInitialized)
                return;

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartGridSuite",
                "WebView2",
                "IgsdPortal");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await PortalWebView.EnsureCoreWebView2Async(env);

            PortalWebView.CoreWebView2.NewWindowRequested +=
                PortalWebView_NewWindowRequested;

            _portalInitialized = true;
        }

        public async Task NavigatePortalAsync(bool forceReload = false)
        {
            if (_portalDisposed)
                return;

            if (string.IsNullOrWhiteSpace(_portalUrl))
                return;

            await EnsurePortalReadyAsync();

            var requestedUrl = _portalUrl.Trim();

            if (forceReload || !string.Equals(_lastPortalRequestedUrl, requestedUrl, StringComparison.OrdinalIgnoreCase))
            {
                PortalWebView.CoreWebView2.Navigate(requestedUrl);
                _lastPortalRequestedUrl = requestedUrl;
            }
        }

        private void PortalWebView_NewWindowRequested(
            object? sender,
            CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            if (string.IsNullOrWhiteSpace(e.Uri))
                return;

            if (_portalDisposed ||
                PortalWebView.CoreWebView2 is null)
            {
                return;
            }

            PortalWebView.CoreWebView2.Navigate(e.Uri);
        }

        public void DisposePortal()
        {
            if (_portalDisposed)
                return;

            _portalDisposed = true;

            try
            {
                if (PortalWebView.CoreWebView2 is not null)
                {
                    PortalWebView.CoreWebView2.NewWindowRequested -=
                        PortalWebView_NewWindowRequested;

                    PortalWebView.CoreWebView2.Stop();
                }
            }
            catch
            {
                /*
                 * The browser process may already be shutting down.
                 * Cleanup should not prevent the Dispatcher shell from closing.
                 */
            }

            try
            {
                PortalWebView.Dispose();
            }
            catch
            {
                /*
                 * Dispose may be called after WebView2 has already released
                 * its native controller.
                 */
            }

            _portalInitialized = false;
            _lastPortalRequestedUrl = string.Empty;
        }

        private async void ReloadPortalButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigatePortalAsync(forceReload: true);
        }

        private void OpenPortalInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_portalUrl))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = _portalUrl,
                UseShellExecute = true
            });
        }
    }
}