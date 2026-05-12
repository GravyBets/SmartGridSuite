using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardPopOutWindow : Window
    {
        private readonly SiteDashboardPaneView _pane;

        public SiteDashboardPopOutWindow(
            ApiClient api,
            IEnumerable<SiteDashboardTabSession> sessions,
            string? selectedSessionKey)
        {
            InitializeComponent();

            _pane = new SiteDashboardPaneView(api);
            RootGrid.Children.Add(_pane);

            _pane.LoadPoppedOutSessions(sessions, selectedSessionKey);
        }

        public void CaptureCurrentState()
        {
            _pane.CaptureCurrentTabUiState();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            CaptureCurrentState();
            base.OnClosing(e);
        }
    }
}