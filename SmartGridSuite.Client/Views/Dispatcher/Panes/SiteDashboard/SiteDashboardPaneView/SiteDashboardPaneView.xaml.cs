using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Settings;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView : UserControl
    {
        private readonly ApiClient _api;
        private readonly TicketsApi _ticketsApi;
        private CancellationTokenSource? _loadCts;


        private bool _isPopOutInstance;
        private SiteDashboardPopOutWindow? _poppedOutWindow;

        private string _currentCnpTechName = string.Empty;

        public bool CanManageSiteNotes
        {
            get => WorkspaceView.CanManageSiteNotes;
            set => WorkspaceView.CanManageSiteNotes = value;
        }

        private readonly List<SiteDashboardTabSession> _sessions = new();
        private string? _selectedSessionKey;
        private int _blankTabCounter = 1;
        private bool _renderingSession;
        private bool _ticketActionInProgress;
        private bool _writeUpSubmitInProgress;

        private string _rangeExtenderLinkUrl = string.Empty;

        private List<CommunicationDeviceTypeDto> _communicationDeviceTypes = new();
        private bool _communicationDeviceTypesLoaded;

        

        public SiteDashboardPaneView()
            : this(new ApiClient("https://localhost:7140"))
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

            WorkspaceView.PingStatsProvider = () => NetworkView.GetPingStatsForWriteUp();

            WorkspaceView.OpenTopTunnelRequested += WorkspaceView_OpenTopTunnelRequested;

            WorkspaceView.RunSnmpOidRequested += WorkspaceView_RunSnmpOidRequested;
            WorkspaceView.RunSnmpCategoryRequested += WorkspaceView_RunSnmpCategoryRequested;
            WorkspaceView.SetSelectedSnmpRequested += WorkspaceView_SetSelectedSnmpRequested;
            WorkspaceView.SnmpTargetChanged += WorkspaceView_SnmpTargetChanged;
            WorkspaceView.SelectedSnmpProfileChanged += WorkspaceView_SelectedSnmpProfileChanged;
            WorkspaceView.RefreshSnmpRequested += WorkspaceView_RefreshSnmpRequested;

            Loaded += SiteDashboardPaneView_Loaded;

            EnsureInitialBlankTab();
            RenderSelectedSession();
        }
    }
}