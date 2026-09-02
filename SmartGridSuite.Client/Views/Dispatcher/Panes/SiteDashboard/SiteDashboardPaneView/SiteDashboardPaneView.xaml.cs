using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Settings;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView : UserControl
    {
        private readonly ApiClient _api;
        private readonly TicketsApi _ticketsApi;
        private CancellationTokenSource? _loadCts;

        private int _siteLoadOverlayDepth;

        private bool _isPopOutInstance;
        private SiteDashboardPopOutWindow? _poppedOutWindow;

        private string _currentCnpTechName = string.Empty;

        public bool CanManageSiteNotes
        {
            get => WorkspaceView.CanManageSiteNotes;
            set => WorkspaceView.CanManageSiteNotes = value;
        }

        private SiteDashboardAccessMode _accessMode = SiteDashboardAccessMode.Full;

        public SiteDashboardAccessMode AccessMode
        {
            get => _accessMode;

            set
            {
                if (_accessMode == value)
                    return;

                _accessMode = value;

                WorkspaceView.AccessMode = value;

                ApplyAccessMode();
            }
        }

        private void ApplyAccessMode()
        {
            var isLineman =
                _accessMode == SiteDashboardAccessMode.Lineman;

            /*
             * Lineman does not receive the network diagnostics pane.
             * Give the workspace the full available width instead.
             */
            NetworkView.Visibility =
                isLineman
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            NetworkColumn.Width =
                isLineman
                    ? new GridLength(0)
                    : new GridLength(340);

            NetworkGapColumn.Width =
                isLineman
                    ? new GridLength(0)
                    : new GridLength(4);
        }

        private readonly List<SiteDashboardTabSession> _sessions = new();
        private string? _selectedSessionKey;
        private bool _renderingSession;
        private bool _ticketActionInProgress;
        private bool _writeUpSubmitInProgress;

        private string _rangeExtenderLinkUrl = string.Empty;

        private List<CommunicationDeviceTypeDto> _communicationDeviceTypes = new();
        private bool _communicationDeviceTypesLoaded;

        

        public SiteDashboardPaneView()
            : this(ClientAppSettings.CreateApiClient())
        {
        }

        public SiteDashboardPaneView(ApiClient api)
        {
            InitializeComponent();
            _api = api;
            _ticketsApi = new TicketsApi(_api);

            TopBarView.LoadRequested += TopBarView_LoadRequested;
            TopBarView.AddTabRequested += TopBarView_AddTabRequested;
            TopBarView.SelectedTabChanged += TopBarView_SelectedTabChanged;
            TopBarView.CloseTabRequested += TopBarView_CloseTabRequested;

            TopBarView.PopOutRequested += TopBarView_PopOutRequested;

            WorkspaceView.RxIpLookupRequested += WorkspaceView_RxIpLookupRequested;
            WorkspaceView.OpenAssociatedSiteRequested += WorkspaceView_OpenAssociatedSiteRequested;

            WorkspaceView.WriteUpTextChanged += WorkspaceView_WriteUpTextChanged;
            WorkspaceView.SelectedWorkspaceTabChanged += WorkspaceView_SelectedWorkspaceTabChanged;
            WorkspaceView.RefreshTicketRequested += WorkspaceView_RefreshTicketRequested;            
            WorkspaceView.TicketActionRequested += WorkspaceView_TicketActionRequested;

            WorkspaceView.WriteUpSubmitRequested -= WorkspaceView_WriteUpSubmitRequested;
            WorkspaceView.WriteUpSubmitRequested += WorkspaceView_WriteUpSubmitRequested;

            NetworkView.NetworkTestRequested += NetworkView_NetworkTestRequested;
            NetworkView.NetworkSessionStateChanged += NetworkView_NetworkSessionStateChanged;

            WorkspaceView.PingStatsProvider = () => NetworkView.GetPingStatsForWriteUp();

            WorkspaceView.IpChangeWriteUpLinesProvider =
                () =>
                {
                    var session =
                        GetSelectedSession();

                    if (session is null)
                        return Array.Empty<string>();

                    return NetworkView.GetIpAddressChangeWriteUpLines(
                        session.PrimaryIp,
                        session.LanIp,
                        session.SecondaryIp,
                        session.IgsdPrimaryRtuIp,
                        session.IgsdPrimaryCommsEthernetIp,
                        session.IgsdSecondaryCommsEthernetIp,
                        session.IgsdSecondaryRtuIp);
                };

            WorkspaceView.OpenTopTunnelRequested += WorkspaceView_OpenTopTunnelRequested;

            WorkspaceView.RunSnmpOidRequested += WorkspaceView_RunSnmpOidRequested;
            WorkspaceView.PollAllSnmpRequested += WorkspaceView_PollAllSnmpRequested;
            WorkspaceView.SetSelectedSnmpRequested += WorkspaceView_SetSelectedSnmpRequested;
            WorkspaceView.SnmpTargetChanged += WorkspaceView_SnmpTargetChanged;
            WorkspaceView.SelectedSnmpProfileChanged += WorkspaceView_SelectedSnmpProfileChanged;
            WorkspaceView.RefreshSnmpRequested += WorkspaceView_RefreshSnmpRequested;

            Loaded += SiteDashboardPaneView_Loaded;

            EnsureInitialBlankTab();
            RenderSelectedSession();
        }

        private void NetworkView_NetworkSessionStateChanged(object? sender, EventArgs e)
        {
            /*
             * Ignore values being populated programmatically while switching
             * or rendering tabs. Only capture actual technician edits.
             */
            if (_renderingSession)
                return;

            var session =
                GetSelectedSession();

            if (session is null)
                return;

            session.NetworkPingState =
                NetworkView.GetPingSessionState();
        }

        private void ShowSiteLoadOverlay(string message)
        {
            _siteLoadOverlayDepth++;

            if (SiteLoadOverlay is null ||
                SiteLoadOverlayMessageTextBlock is null)
            {
                return;
            }

            SiteLoadOverlayMessageTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Loading site data..."
                : message;

            SiteLoadOverlay.Visibility = Visibility.Visible;

            TopBarView.IsEnabled = false;
            NetworkView.IsEnabled = false;
            WorkspaceView.IsEnabled = false;

            Cursor = Cursors.Wait;
        }

        private void HideSiteLoadOverlay()
        {
            if (_siteLoadOverlayDepth > 0)
                _siteLoadOverlayDepth--;

            if (_siteLoadOverlayDepth > 0)
                return;

            if (SiteLoadOverlay is null)
                return;

            SiteLoadOverlay.Visibility = Visibility.Collapsed;

            TopBarView.IsEnabled = true;
            NetworkView.IsEnabled = true;
            WorkspaceView.IsEnabled = true;

            Cursor = null;
        }

        private void UpdateSiteLoadOverlayMessage(string message)
        {
            if (SiteLoadOverlayMessageTextBlock is null)
                return;

            SiteLoadOverlayMessageTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Loading site data..."
                : message;
        }

        public void Shutdown()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            WorkspaceView.DisposePortal();
        }
    }
}