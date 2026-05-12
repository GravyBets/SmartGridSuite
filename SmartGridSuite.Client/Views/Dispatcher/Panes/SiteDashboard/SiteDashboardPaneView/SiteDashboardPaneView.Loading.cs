using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Settings;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using System.Net;
using System.Net.Http;


namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private async void TopBarView_LoadRequested(object? sender, EventArgs e)
        {
            await LoadAsync(TopBarView.SearchText);
        }

        private async Task<SiteDashboardResponseDto?> GetSiteOrTowerDashboardAsync(string searchText, CancellationToken ct)
        {
            var cleanSearch = (searchText ?? string.Empty).Trim();

            Exception? lastNotFoundException = null;

            foreach (var candidate in BuildSiteDashboardSearchCandidates(cleanSearch))
            {
                try
                {
                    return await _api.GetSiteDashboardAsync(candidate, ct);
                }
                catch (ApiClient.ApiException ex) when (ex.StatusCode == 404)
                {
                    lastNotFoundException = ex;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    lastNotFoundException = ex;
                }
            }

            var tower = await TryFindTowerDashboardAsync(cleanSearch, ct);

            if (tower is not null)
                return tower;

            if (lastNotFoundException is not null)
                throw lastNotFoundException;

            return await _api.GetSiteDashboardAsync(cleanSearch, ct);
        }

        private static IEnumerable<string> BuildSiteDashboardSearchCandidates(string searchText)
        {
            var cleanSearch = (searchText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanSearch))
                yield break;

            // 1. Try exactly what the tech typed first.
            // This allows DACS/exact SiteId searches like "2837" to win.
            yield return cleanSearch;

            // 2. If the tech typed only digits, try the MR suffix next.
            if (ShouldTryMrSuffixFallback(cleanSearch))
                yield return $"{cleanSearch}MR";
        }

        private static bool IsDashboardNotFoundException(Exception ex)
        {
            return ex is ApiClient.ApiException apiEx && apiEx.StatusCode == 404
                || ex is HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.NotFound;
        }

        private static string ResolveBlankDashboardSiteId(string searchText)
        {
            var cleanSearch = (searchText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanSearch))
                return "New Site";

            // Match the search behavior:
            // "2837" tries "2837" first, then "2837MR".
            // If neither exists, default the blank dashboard to 2837MR.
            if (ShouldTryMrSuffixFallback(cleanSearch))
                return $"{cleanSearch}MR";

            return cleanSearch;
        }

        private static void ApplyBlankDashboardToSession(SiteDashboardTabSession session, string siteId)
        {
            ResetSessionForNewSiteLoad(session);

            session.DashboardKind = string.Empty;

            session.HeaderText = siteId;
            session.SearchText = siteId;

            session.AddressText = "—";
            session.CoordinatesText = "—";

            // Leave these blank so the tech can type directly into the IP boxes.
            session.PrimaryIp = string.Empty;
            session.LanIp = string.Empty;
            session.SecondaryIp = string.Empty;

            session.IgsdPrimaryRtuIp = string.Empty;
            session.IgsdPrimaryCommsEthernetIp = string.Empty;
            session.IgsdSecondaryCommsEthernetIp = string.Empty;
            session.IgsdSecondaryRtuIp = string.Empty;
            session.IgsdPrimaryTunnelIp = string.Empty;

            session.TopTunnelIp = "—";
            session.SiteStatusText = "New Site";
            session.TopAccessTitleText = "TOP Access";
            session.TopInfoText = string.Empty;

            // Keep equipment layout available, but with no returned serials.
            session.EquipmentText = string.Empty;
            session.EquipmentReplacementEntries = new List<EquipmentReplacementSessionEntry>();

            session.HistoryRows = new List<SiteDashboardHistoryRowViewModel>();
            session.TicketInfoText = "No ticket data returned yet.";
            session.CurrentTicketId = 0;

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = string.Empty;
            session.RangeExtenderLinkUrl = string.Empty;

            session.TowerSummaryText = string.Empty;
            session.TowerSectors = new List<TowerSectorDto>();

            session.NetworkPingState = null;
            session.TowerPingState = null;

            session.SelectedWorkspaceTabKey = "TopWriteUp";

            session.SnmpTargetIp = string.Empty;
            session.SnmpSupported = false;
            session.SnmpSupportMessage = "Blank site dashboard. Select a profile and enter a target IP.";
            session.SnmpDeviceFamily = string.Empty;
            session.SnmpProfileName = string.Empty;
            session.SnmpPrimaryCommType = string.Empty;
            session.SnmpOids = new List<SnmpOidConfigDto>();
            session.SnmpProfiles = new List<SnmpProfileListItemDto>();
            session.SnmpProfileId = null;
            session.SnmpOidResults = new Dictionary<ulong, string>();
        }

        private static bool ShouldTryMrSuffixFallback(string searchText)
        {
            var cleanSearch = (searchText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanSearch))
                return false;

            if (cleanSearch.EndsWith("MR", StringComparison.OrdinalIgnoreCase))
                return false;

            return cleanSearch.All(char.IsDigit);
        }

        private async Task<SiteDashboardResponseDto?> TryFindTowerDashboardAsync(string searchText, CancellationToken ct)
        {
            var results = await _api.SearchTowersAsync(searchText, take: 10, ct);

            if (results.Count == 0)
                return null;

            var normalizedSearch = NormalizeTowerSearchText(searchText);

            var exact = results.FirstOrDefault(x =>
                NormalizeTowerSearchText(x.TopName) == normalizedSearch ||
                NormalizeTowerSearchText(x.TopDescription) == normalizedSearch);

            var selected = exact ?? results.FirstOrDefault();

            if (selected is null || selected.TopNameId <= 0)
                return null;

            return await _api.GetTowerDashboardAsync(selected.TopNameId, ct);
        }

        private static string NormalizeTowerSearchText(string? value)
        {
            return (value ?? string.Empty)
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private async void SiteDashboardPaneView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Loaded -= SiteDashboardPaneView_Loaded;

            await LoadCommunicationDeviceTypesForWorkspaceAsync();
            await LoadRangeExtenderLinkUrlForWorkspaceAsync();
            await LoadCurrentCnpTechNameAsync();
        }

        private async Task LoadCommunicationDeviceTypesForWorkspaceAsync()
        {
            if (_communicationDeviceTypesLoaded)
                return;

            try
            {
                var items = await _api.GetCommunicationDeviceTypesAsync(activeOnly: true);

                _communicationDeviceTypes = items
                    .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.DisplayName))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

                WorkspaceView.SetCommunicationDeviceTypes(_communicationDeviceTypes);
                _communicationDeviceTypesLoaded = true;
            }
            catch
            {
                _communicationDeviceTypes = new List<CommunicationDeviceTypeDto>();
                WorkspaceView.SetCommunicationDeviceTypes(_communicationDeviceTypes);
                _communicationDeviceTypesLoaded = true;
            }
        }

        //Load Async
        private async Task LoadAsync(string rawSiteId)
        {
            WorkspaceView.StopTowerPings();
            var siteId = (rawSiteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                TopBarView.StatusText = "Enter a site ID first.";
                return;
            }

            var siteSearchCandidates = BuildSiteDashboardSearchCandidates(siteId).ToList();

            var existingSession = _sessions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                siteSearchCandidates.Any(candidate =>
                    string.Equals(x.HeaderText, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.SearchText, candidate, StringComparison.OrdinalIgnoreCase)));

            if (existingSession is not null)
            {
                SaveCurrentTabUiState();

                _selectedSessionKey = existingSession.SessionKey;
                RenderSelectedSession();
                TopBarView.StatusText = $"Switched to {existingSession.HeaderText}.";
                return;
            }

            var selectedSession = GetSelectedSession();
            if (selectedSession is null)
            {
                CreateBlankTab(selectNewTab: true);
                selectedSession = GetSelectedSession();
            }

            if (selectedSession is null)
                return;

            var previousLoadedSite = (selectedSession.SearchText ?? string.Empty).Trim();

            var isBlankSessionLoad =
                string.IsNullOrWhiteSpace(previousLoadedSite) ||
                previousLoadedSite.StartsWith("Blank", StringComparison.OrdinalIgnoreCase);

            var isDifferentSiteLoad =
                !isBlankSessionLoad &&
                !string.Equals(previousLoadedSite, siteId, StringComparison.OrdinalIgnoreCase);

            var shouldClearForSiteLoad = isBlankSessionLoad || isDifferentSiteLoad;

            if (shouldClearForSiteLoad)
            {
                ResetSessionForNewSiteLoad(selectedSession);
            }

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();

            try
            {
                TopBarView.SetLoading(true);
                TopBarView.StatusText = $"Loading {siteId}...";

                var dashboard = await GetSiteOrTowerDashboardAsync(siteId, _loadCts.Token);
                var loadedSiteId = GetObjectPropertyText(dashboard, "SiteId") ?? siteId;

                selectedSession.TicketInfoText = "Loading ticket data...";

                ApplyDashboardToSession(selectedSession, dashboard, loadedSiteId);
                await ApplyPingScreenPortalUrlAsync(selectedSession, dashboard, _loadCts.Token);

                if (shouldClearForSiteLoad)
                {
                    selectedSession.SelectedWorkspaceTabKey = "TopWriteUp";
                }

                selectedSession.SnmpTargetIp =
                    shouldClearForSiteLoad || string.IsNullOrWhiteSpace(selectedSession.SnmpTargetIp)
                        ? selectedSession.PrimaryIp
                        : selectedSession.SnmpTargetIp;

                selectedSession.SnmpSupportMessage = "Loading SNMP configuration...";

                _selectedSessionKey = selectedSession.SessionKey;
                RenderSelectedSession();

                await RefreshTicketInfoAsync(selectedSession, _loadCts.Token);

                if (ShouldLoadSnmpForDashboard(selectedSession))
                {
                    await RefreshSnmpConfigAsync(selectedSession, _loadCts.Token);
                }
                else
                {
                    ClearSnmpForUnsupportedDashboard(selectedSession);
                }

                if (selectedSession.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                if (shouldClearForSiteLoad && ShouldRunNetworkAutoQuickTest(selectedSession))
                {
                    await Dispatcher.InvokeAsync(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.Loaded);

                    await NetworkView.RunQuickReachabilityTestForAllAsync();

                    selectedSession.NetworkPingState = NetworkView.GetPingSessionState();
                }

                TopBarView.StatusText = $"Loaded {loadedSiteId}.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (IsDashboardNotFoundException(ex))
            {
                var blankSiteId = ResolveBlankDashboardSiteId(siteId);

                TopBarView.StatusText = $"No existing site found. Opening blank dashboard for {blankSiteId}...";

                ApplyBlankDashboardToSession(selectedSession, blankSiteId);

                _selectedSessionKey = selectedSession.SessionKey;
                RenderSelectedSession();

                // Optional but useful: still allow existing tickets/SNMP profiles to load
                // even though the site itself was not found.
                try
                {
                    await RefreshTicketInfoAsync(selectedSession, _loadCts?.Token ?? CancellationToken.None);
                    await RefreshSnmpConfigAsync(selectedSession, _loadCts?.Token ?? CancellationToken.None);

                    if (selectedSession.SessionKey == _selectedSessionKey)
                        RenderSelectedSession();
                }
                catch
                {
                    // Keep the blank dashboard usable even if ticket/SNMP refresh fails.
                }

                TopBarView.StatusText = $"Blank dashboard ready for {blankSiteId}.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Load failed: {ex.Message}";
            }
            finally
            {
                TopBarView.SetLoading(false);
            }
        }

        private static bool ShouldRunNetworkAutoQuickTest(SiteDashboardTabSession session)
        {
            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Rx, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase))
                return false;

            return HasUsableNetworkIp(session.PrimaryIp) ||
                   HasUsableNetworkIp(session.LanIp) ||
                   HasUsableNetworkIp(session.SecondaryIp);
        }

        private static bool HasUsableNetworkIp(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            return !string.IsNullOrWhiteSpace(text) &&
                   text != "—";
        }

        private static bool ShouldLoadSnmpForDashboard(SiteDashboardTabSession session)
        {
            return !string.Equals(
                session.DashboardKind,
                SiteDashboardKinds.Rx,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void ClearSnmpForUnsupportedDashboard(SiteDashboardTabSession session)
        {
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
    }
}