using SmartGridSuite.Client.Services;
using System.Threading;
using System.Windows;

namespace SmartGridSuite.Client
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName =
            @"Local\SmartGridSuite.Client.SingleInstance";

        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;

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