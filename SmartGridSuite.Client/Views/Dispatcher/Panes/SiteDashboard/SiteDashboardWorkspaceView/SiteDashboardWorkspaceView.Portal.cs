using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private bool _portalInitialized;
        private string _portalUrl = string.Empty;
        private string _lastPortalRequestedUrl = string.Empty;

        public async Task EnsurePortalReadyAsync()
        {
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

            PortalWebView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;

                if (!string.IsNullOrWhiteSpace(e.Uri))
                    PortalWebView.CoreWebView2.Navigate(e.Uri);
            };

            _portalInitialized = true;
        }

        public async Task NavigatePortalAsync(bool forceReload = false)
        {
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