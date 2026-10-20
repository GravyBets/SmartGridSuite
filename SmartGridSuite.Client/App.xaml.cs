using SmartGridSuite.Client.Services;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace SmartGridSuite.Client
{
    public partial class App : Application
    {
        

        private const string SingleInstanceMutexName =
            @"Local\SmartGridSuite.Client.SingleInstance";

        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;
        private Uri? _pendingClickOnceUpdateUri;

        // Called only after the About dialog has confirmed a newer published
        // version and an absolute ClickOnce .application deployment URL.
        internal void ScheduleClickOnceUpdate(Uri deploymentUri)
        {
            ArgumentNullException.ThrowIfNull(deploymentUri);
            _pendingClickOnceUpdateUri = deploymentUri;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: SingleInstanceMutexName,
                createdNew: out var isFirstInstance);

            if (!isFirstInstance)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                MessageBox.Show(
                    "Smart Grid Suite is already open.",
                    "Smart Grid Suite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }

            _ownsSingleInstanceMutex = true;

            // Register global interface-scaling handlers
            // before any application windows are created.
            InterfaceScaleService.Initialize();

            // Keep the native Windows caption/title bar readable and
            // synchronized with the active SmartGridSuite theme.
            WindowTitleBarService.Initialize();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsSingleInstanceMutex &&
                _singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _ownsSingleInstanceMutex = false;
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;

            var updateUri = _pendingClickOnceUpdateUri;
            _pendingClickOnceUpdateUri = null;

            if (updateUri != null)
            {
                try
                {
                    // The original application has released its single-instance
                    // mutex. Let ClickOnce fetch and launch the new published app.
                    var start = new ProcessStartInfo("rundll32.exe")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    start.ArgumentList.Add("dfshim.dll,ShOpenVerbApplication");
                    start.ArgumentList.Add(updateUri.AbsoluteUri);

                    using var updater = Process.Start(start)
                        ?? throw new InvalidOperationException(
                            "Windows could not launch the ClickOnce deployment.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Smart Grid Suite has closed, but the update could not " +
                        "be started automatically. Open Smart Grid Suite from " +
                        "its usual shortcut to update, or use your internal " +
                        "installation page.\n\n" + ex.Message,
                        "Smart Grid Suite Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            base.OnExit(e);
        }
    }
}