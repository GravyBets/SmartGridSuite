using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private void WorkspaceView_SelectedWorkspaceTabChanged(object? sender, string? tabKey)
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();
            if (session is null)
                return;

            session.SelectedWorkspaceTabKey = string.IsNullOrWhiteSpace(tabKey)
                ? "TopWriteUp"
                : tabKey;
        }

        private void TopBarView_AddTabRequested(object? sender, EventArgs e)
        {
            SaveCurrentTabUiState();

            CreateBlankTab(selectNewTab: true);
            RenderSelectedSession();
        }

        private void TopBarView_SelectedTabChanged(object? sender, string? sessionKey)
        {
            if (string.Equals(_selectedSessionKey, sessionKey, StringComparison.Ordinal))
                return;

            SaveCurrentTabUiState();

            _selectedSessionKey = sessionKey;
            RenderSelectedSession();
        }

        private void TopBarView_CloseTabRequested(object? sender, string? sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return;

            SaveCurrentTabUiState();

            var index = _sessions.FindIndex(x => x.SessionKey == sessionKey);
            if (index < 0)
                return;

            if (_poppedOutWindow is not null)
            {
                _poppedOutWindow.Close();
            }

            var wasSelected = string.Equals(_selectedSessionKey, sessionKey, StringComparison.Ordinal);

            _sessions.RemoveAt(index);

            if (_sessions.Count == 0)
            {
                CreateBlankTab(selectNewTab: true);
            }
            else if (wasSelected)
            {
                var newIndex = Math.Min(index, _sessions.Count - 1);
                _selectedSessionKey = _sessions[newIndex].SessionKey;
            }

            RenderSelectedSession();
        }

        private void EnsureInitialBlankTab()
        {
            if (_sessions.Count > 0)
                return;

            CreateBlankTab(selectNewTab: true);
        }

        private void CreateBlankTab(bool selectNewTab)
        {
            var header = _blankTabCounter == 1 ? "Blank" : $"Blank ({_blankTabCounter})";

            var session = new SiteDashboardTabSession
            {
                SessionKey = Guid.NewGuid().ToString("N"),
                HeaderText = header,
                SearchText = string.Empty
            };

            _blankTabCounter++;
            _sessions.Add(session);

            if (selectNewTab)
                _selectedSessionKey = session.SessionKey;
        }

        private SiteDashboardTabSession? GetSelectedSession()
        {
            if (string.IsNullOrWhiteSpace(_selectedSessionKey))
                return null;

            return _sessions.FirstOrDefault(x => x.SessionKey == _selectedSessionKey);
        }

        private void SaveCurrentTabUiState()
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();

            if (session is null)
                return;

            session.WriteUpText = WorkspaceView.WriteUpText;
            session.SelectedWorkspaceTabKey = WorkspaceView.SelectedWorkspaceTabKey;

            session.EquipmentReplacementEntries =
                WorkspaceView.GetEquipmentReplacementSessionEntries();

            session.SnmpTargetIp = WorkspaceView.GetSnmpTargetIp();
            session.SnmpProfileId = WorkspaceView.GetSelectedSnmpProfileId();
            session.SnmpOidResults = WorkspaceView.GetSnmpOidResultSnapshot();

            session.NetworkPingState = NetworkView.GetPingSessionState();
            session.TowerPingState = WorkspaceView.GetTowerPingSessionState();
        }

        public void LoadPoppedOutSessions(IEnumerable<SiteDashboardTabSession> sessions, string? selectedSessionKey)
        {
            _isPopOutInstance = true;

            TopBarView.SetPopOutButtonVisible(false);

            _sessions.Clear();
            _sessions.AddRange(sessions);

            _selectedSessionKey = !string.IsNullOrWhiteSpace(selectedSessionKey)
                ? selectedSessionKey
                : _sessions.FirstOrDefault()?.SessionKey;

            RenderSelectedSession();
        }

        public void CaptureCurrentTabUiState()
        {
            SaveCurrentTabUiState();
        }        

        //Resets the ENTIRE workspace Tab back to Main and clears the SNMP state. 
        private static void ResetSessionForNewSiteLoad(SiteDashboardTabSession session)
        {
            session.AddressText = "—";
            session.CoordinatesText = "—";

            session.PrimaryIp = "—";
            session.LanIp = "—";
            session.SecondaryIp = "—";
            session.NetworkPingState = null;
            session.TowerPingState = null;

            session.TopInfoText = string.Empty;
            session.WriteUpText = string.Empty;
            session.EquipmentText = string.Empty;
            session.EquipmentReplacementEntries = new List<EquipmentReplacementSessionEntry>();
            session.SelectedWorkspaceTabKey = "TopWriteUp";

            session.SiteStatusText = string.Empty;
            session.TopAccessTitleText = "TOP Access";
            session.TicketInfoText = "Loading ticket data...";
            session.HistoryRows = new List<SiteDashboardHistoryRowViewModel>();
            session.CurrentTicketId = 0;

            session.DashboardKind = string.Empty;

            session.IgsdPrimaryRtuIp = "—";
            session.IgsdSecondaryCommsEthernetIp = "—";
            session.IgsdSecondaryRtuIp = "—";
            session.IgsdPrimaryTunnelIp = "—";
            session.TopTunnelIp = "—";

            session.SnmpSupported = false;
            session.SnmpSupportMessage = string.Empty;
            session.SnmpDeviceFamily = string.Empty;
            session.SnmpProfileName = string.Empty;
            session.SnmpPrimaryCommType = string.Empty;
            session.SnmpTargetIp = string.Empty;
            session.SnmpOids = new List<SnmpOidConfigDto>();
            session.SnmpProfiles = new List<SnmpProfileListItemDto>();
            session.SnmpProfileId = null;
            session.SnmpOidResults = new Dictionary<ulong, string>();
        }

        private static void ClearSessionTemporaryDashboardState(SiteDashboardTabSession session)
        {
            // Main write-up area
            session.WriteUpText = string.Empty;

            session.EquipmentReplacementEntries = new List<EquipmentReplacementSessionEntry>();

            session.NetworkPingState = null;
            session.TowerPingState = null;

            // SNMP temporary state/results
            session.SnmpTargetIp = string.Empty;
            session.SnmpProfileId = null;
            session.SnmpProfileName = string.Empty;
            session.SnmpDeviceFamily = string.Empty;
            session.SnmpSupportMessage = string.Empty;
            session.SnmpSupported = false;
            session.SnmpProfiles = new();
            session.SnmpOids = new();
            session.SnmpOidResults = new();

            // Workspace selection gets set after reload.
            session.SelectedWorkspaceTabKey = "SiteHistory";
        }

        private void TopBarView_PopOutRequested(object? sender, EventArgs e)
        {
            if (_isPopOutInstance)
                return;

            SaveCurrentTabUiState();

            if (_sessions.Count == 0)
                return;

            var realSessions = _sessions
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.SessionKey) &&
                    !string.IsNullOrWhiteSpace(x.HeaderText))
                .ToList();

            if (realSessions.Count == 0)
            {
                TopBarView.StatusText = "Load or create a site before popping out.";
                return;
            }

            if (_poppedOutWindow is not null)
            {
                BringPopOutWindowForward(_poppedOutWindow);
                return;
            }

            foreach (var session in realSessions)
                session.IsPoppedOut = true;

            var window = new SiteDashboardPopOutWindow(
                _api,
                realSessions,
                _selectedSessionKey)
            {
                Owner = Window.GetWindow(this),
                Title = "Site Dashboard",
                Width = 1500,
                Height = 900,
                MinWidth = 1100,
                MinHeight = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _poppedOutWindow = window;

            window.Closed += (_, _) =>
            {
                try
                {
                    window.CaptureCurrentState();
                }
                catch
                {
                    // Keep close behavior safe.
                }

                foreach (var session in realSessions)
                    session.IsPoppedOut = false;

                _poppedOutWindow = null;

                RenderSelectedSession();
            };

            RenderSelectedSession();

            window.Show();
            BringPopOutWindowForward(window);
        }

        private static void BringPopOutWindowForward(Window window)
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }
    }
}