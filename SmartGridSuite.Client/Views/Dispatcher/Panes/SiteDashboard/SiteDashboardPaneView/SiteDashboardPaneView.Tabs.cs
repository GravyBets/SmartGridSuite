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

            /*
             * WorkspaceView is shared by every Site Dashboard tab.
             * Rendering a different tab can reset the Poll All button back
             * to its default appearance, even though the originating session
             * is still actively polling.
             *
             * Always restore the button from the newly selected session's
             * runtime SNMP state after the tab has been rendered.
             */
            var selectedSession =
                GetSelectedSession();

            WorkspaceView.SetSnmpPollAllRunning(
                selectedSession?.IsSnmpPollAllRunning == true);
        }

        private void TopBarView_CloseTabRequested(object? sender, string? sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return;

            /*
             * If this is the selected tab, capture whatever is
             * currently visible in the write-up TextBox first.
             */
            SaveCurrentTabUiState();

            var index =
                _sessions.FindIndex(
                    x => x.SessionKey == sessionKey);

            if (index < 0)
                return;

            var sessionToClose =
                _sessions[index];

            if (!ConfirmDiscardWriteUp(
                    sessionToClose,
                    "Closing this tab"))
            {
                return;
            }

            if (_poppedOutWindow is not null)
            {
                _poppedOutWindow.Close();
            }

            var wasSelected =
                string.Equals(
                    _selectedSessionKey,
                    sessionKey,
                    StringComparison.Ordinal);

            _sessions.RemoveAt(index);

            if (_sessions.Count == 0)
            {
                CreateBlankTab(
                    selectNewTab: true);
            }
            else if (wasSelected)
            {
                var newIndex =
                    Math.Min(
                        index,
                        _sessions.Count - 1);

                _selectedSessionKey =
                    _sessions[newIndex].SessionKey;
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
            var blankNumber = GetNextBlankTabNumber();
            var header = blankNumber == 1 ? "Blank" : $"Blank ({blankNumber})";

            var session = new SiteDashboardTabSession
            {
                SessionKey = Guid.NewGuid().ToString("N"),
                HeaderText = header,
                SearchText = string.Empty
            };

            _sessions.Add(session);

            if (selectNewTab)
                _selectedSessionKey = session.SessionKey;
        }

        private int GetNextBlankTabNumber()
        {
            var usedNumbers = _sessions
                .Select(x => GetBlankTabNumber(x.HeaderText))
                .Where(x => x > 0)
                .ToHashSet();

            var number = 1;

            while (usedNumbers.Contains(number))
                number++;

            return number;
        }

        private static int GetBlankTabNumber(string? headerText)
        {
            var text = (headerText ?? string.Empty).Trim();

            if (text.Equals("Blank", StringComparison.OrdinalIgnoreCase))
                return 1;

            const string prefix = "Blank (";

            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !text.EndsWith(")", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var numberText = text.Substring(prefix.Length, text.Length - prefix.Length - 1);

            return int.TryParse(numberText, out var number) && number > 1
                ? number
                : 0;
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
            session.SubmitOptions = WorkspaceView.GetSubmitOptionsSessionState();

            session.EquipmentReplacementEntries =
                WorkspaceView.GetEquipmentReplacementSessionEntries();

            session.SnmpTargetIp = WorkspaceView.GetSnmpTargetIp();
            session.SnmpProfileId = WorkspaceView.GetSelectedSnmpProfileId();
            session.SnmpOidResults = WorkspaceView.GetSnmpOidResultSnapshot();

            session.NetworkPingState = NetworkView.GetPingSessionState();
            session.TowerPingState = WorkspaceView.GetTowerPingSessionState();
        }

        private static bool HasUnsubmittedWriteUpText(
            SiteDashboardTabSession? session)
        {
            return session is not null &&
                   !string.IsNullOrWhiteSpace(
                       session.WriteUpText);
        }

        private static string GetSessionSiteLabel(
            SiteDashboardTabSession session)
        {
            var label =
                (session.HeaderText ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(label)
                ? "this site"
                : label;
        }

        private bool ConfirmDiscardWriteUp(
            SiteDashboardTabSession session,
            string destructiveAction)
        {
            if (!HasUnsubmittedWriteUpText(session))
                return true;

            var siteLabel =
                GetSessionSiteLabel(session);

            var result =
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"There is text in the write-up for {siteLabel} " +
                    "that has not been submitted."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"{destructiveAction} will permanently discard " +
                    "that write-up. Continue?",
                    "Unsaved Write-Up",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        public bool ConfirmDiscardWriteUpsForShellClose()
        {
            /*
             * Capture the currently visible TextBox before checking
             * every dashboard session.
             */
            SaveCurrentTabUiState();

            var sessionsWithWriteUps =
                _sessions
                    .Where(HasUnsubmittedWriteUpText)
                    .ToList();

            if (sessionsWithWriteUps.Count == 0)
                return true;

            string message;

            if (sessionsWithWriteUps.Count == 1)
            {
                var siteLabel =
                    GetSessionSiteLabel(
                        sessionsWithWriteUps[0]);

                message =
                    $"There is text in the write-up for {siteLabel} " +
                    "that has not been submitted.";
            }
            else
            {
                message =
                    $"{sessionsWithWriteUps.Count} Site Dashboard tabs " +
                    "contain write-up text that has not been submitted.";
            }

            message +=
                Environment.NewLine +
                Environment.NewLine +
                "Leaving the Field Technician module will permanently " +
                "discard the unsubmitted write-up text. Continue?";

            return MessageBox.Show(
                       Window.GetWindow(this),
                       message,
                       "Unsaved Write-Up",
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Warning)
                   == MessageBoxResult.Yes;
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
            session.SubmitOptions = new SiteDashboardSubmitOptionsSessionState();
            session.SelectedWorkspaceTabKey = "TopWriteUp";

            session.SiteStatusText = string.Empty;
            session.TopAccessTitleText = "TOP Access";
            session.TicketInfoText = "Loading ticket data...";
            session.HistoryRows = new List<SiteDashboardHistoryRowViewModel>();
            session.CurrentTicketId = 0;

            session.HasExplicitTicketContext = false;

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
            session.SubmitOptions = new SiteDashboardSubmitOptionsSessionState();
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

        private bool CanUseCurrentSessionForOpenAllLoad()
        {
            var session = GetSelectedSession();

            if (session is null)
                return false;

            var header = (session.HeaderText ?? string.Empty).Trim();
            var search = (session.SearchText ?? string.Empty).Trim();

            var headerLooksBlank =
                string.IsNullOrWhiteSpace(header) ||
                header.StartsWith("Blank", StringComparison.OrdinalIgnoreCase);

            var searchLooksBlank =
                string.IsNullOrWhiteSpace(search) ||
                search.StartsWith("Blank", StringComparison.OrdinalIgnoreCase);

            return headerLooksBlank && searchLooksBlank;
        }

        private SiteDashboardTabSession? FindExistingSessionForSearchText(string searchText)
        {
            var candidates = BuildSiteDashboardSearchCandidates(searchText)
                .ToList();

            if (candidates.Count == 0)
                return null;

            return _sessions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                candidates.Any(candidate =>
                    string.Equals(x.HeaderText, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.SearchText, candidate, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task RunQuickTestsForOpenAllSessionsAsync(IReadOnlyList<string> sessionKeys, string? returnToSessionKey)
        {
            var uniqueSessionKeys = sessionKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < uniqueSessionKeys.Count; index++)
            {
                var session = _sessions.FirstOrDefault(x =>
                    string.Equals(x.SessionKey, uniqueSessionKeys[index], StringComparison.Ordinal));

                if (session is null)
                    continue;

                if (!ShouldRunNetworkAutoQuickTest(session))
                    continue;

                UpdateSiteLoadOverlayMessage(
                    $"Running quick network test {index + 1} of {uniqueSessionKeys.Count}: {session.HeaderText}...");

                _selectedSessionKey = session.SessionKey;
                RenderSelectedSession();

                await Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Loaded);

                await NetworkView.RunQuickReachabilityTestForAllAsync();

                session.NetworkPingState =
                    NetworkView.GetPingSessionState();
            }

            if (!string.IsNullOrWhiteSpace(returnToSessionKey))
            {
                _selectedSessionKey = returnToSessionKey;
                RenderSelectedSession();
            }
        }

        public async Task OpenSitesFromFieldTechTasksAsync(IReadOnlyList<string> sites)
        {
            var cleanSites = (sites ?? Array.Empty<string>())
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanSites.Count == 0)
            {
                TopBarView.StatusText = "No Daily Assignment sites were provided.";
                return;
            }

            ShowSiteLoadOverlay($"Opening {cleanSites.Count} Daily Assignment site tab(s)...");

            var openedSessionKeys = new List<string>();
            string? firstOpenedSessionKey = null;

            try
            {
                SaveCurrentTabUiState();

                for (var index = 0; index < cleanSites.Count; index++)
                {
                    var site = cleanSites[index];

                    UpdateSiteLoadOverlayMessage(
                        $"Opening site {index + 1} of {cleanSites.Count}: {site}...");

                    var existingSession =
                        FindExistingSessionForSearchText(site);

                    if (existingSession is null &&
                        !CanUseCurrentSessionForOpenAllLoad())
                    {
                        CreateBlankTab(selectNewTab: true);
                        RenderSelectedSession();

                        await Dispatcher.InvokeAsync(
                            () => { },
                            System.Windows.Threading.DispatcherPriority.Loaded);
                    }

                    await LoadAsync(
                        site,
                        runAutoQuickTest: false,
                        useSiteLoadOverlay: false);

                    var loadedSession = GetSelectedSession();

                    if (loadedSession is null)
                        continue;

                    openedSessionKeys.Add(loadedSession.SessionKey);

                    firstOpenedSessionKey ??=
                        loadedSession.SessionKey;
                }

                if (!string.IsNullOrWhiteSpace(firstOpenedSessionKey))
                {
                    _selectedSessionKey = firstOpenedSessionKey;
                    RenderSelectedSession();
                }

                UpdateSiteLoadOverlayMessage("All tabs opened. Starting quick network tests...");

                await RunQuickTestsForOpenAllSessionsAsync(
                    openedSessionKeys,
                    firstOpenedSessionKey);

                TopBarView.StatusText =
                    $"Opened {openedSessionKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()} Daily Assignment site tab(s).";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText =
                    $"Open All failed: {ex.Message}";

                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Message,
                    "Open All Site Dashboards",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideSiteLoadOverlay();
            }
        }
    }
}