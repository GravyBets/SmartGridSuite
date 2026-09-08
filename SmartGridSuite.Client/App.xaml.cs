using SmartGridSuite.Client.Services;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace SmartGridSuite.Client
{
    public partial class App : Application
    {
        /*
         * Stable Windows taskbar identity.
         *
         * ClickOnce installs each application revision into a versioned
         * cache path. Without an explicit AppUserModelID, Windows can treat
         * the pinned SmartGridSuite launcher and the running client as
         * different applications.
         *
         * Do not change this value between releases.
         */
        private const string AppUserModelId =
            "SmartGridSuite.Desktop.Client";

        private const string SingleInstanceMutexName =
            @"Local\SmartGridSuite.Client.SingleInstance";

        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;

        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = false)]
        private static extern int
            SetCurrentProcessExplicitAppUserModelID(
                string appID);

        protected override void OnStartup(StartupEventArgs e)
        {
            /*
             * Set the taskbar identity before any WPF windows are created.
             * All Launcher / Dispatcher / Field Technician /
             * Administration windows will inherit this identity.
             */
            _ =
                SetCurrentProcessExplicitAppUserModelID(
                    AppUserModelId);

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

            base.OnExit(e);
        }
    }
}