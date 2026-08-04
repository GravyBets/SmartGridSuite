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

        private async Task<SiteDashboardResponseDto?>GetSiteOrTowerDashboardAsync(
            string searchText, CancellationToken ct)
        {
            var cleanSearch =
                (searchText ?? string.Empty).Trim();

            Exception? lastNotFoundException = null;

            Exception? lastParentDatabaseUnavailableException =
                null;

            foreach (var candidate
                     in BuildSiteDashboardSearchCandidates(cleanSearch))
            {
                try
                {
                    return await _api.GetSiteDashboardAsync(
                        candidate,
                        ct);
                }
                catch (ApiClient.ApiException ex)
                    when (ex.StatusCode == 404)
                {
                    lastNotFoundException = ex;
                }
                catch (HttpRequestException ex)
                    when (ex.StatusCode ==
                          HttpStatusCode.NotFound)
                {
                    lastNotFoundException = ex;
                }
                catch (Exception ex)
                    when (IsParentDatabaseUnavailableException(ex))
                {
                    /*
                     * Keep trying the remaining search candidates.
                     *
                     * Example:
                     *   Tech enters 2837
                     *   2837 has no cache
                     *   2837MR may still have cached site data
                     */
                    lastParentDatabaseUnavailableException = ex;
                }
            }

            /*
             * Do not attempt the live Parent DB tower search when the
             * Parent DB has already reported that it is unavailable.
             */
            if (lastParentDatabaseUnavailableException is not null)
            {
                throw lastParentDatabaseUnavailableException;
            }

            var tower =
                await TryFindTowerDashboardAsync(
                    cleanSearch,
                    ct);

            if (tower is not null)
            {
                return tower;
            }

            if (lastNotFoundException is not null)
            {
                throw lastNotFoundException;
            }

            return await _api.GetSiteDashboardAsync(
                cleanSearch,
                ct);
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

        private static bool IsParentDatabaseUnavailableException(Exception ex)
        {
            return ex is ApiClient.ApiException apiException
                && apiException.StatusCode == 503
                && !string.IsNullOrWhiteSpace(
                    apiException.Body)
                && apiException.Body.Contains(
                    "PARENT_DB_UNAVAILABLE",
                    StringComparison.OrdinalIgnoreCase);
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

        private static string ResolveLimitedModeSiteId(string searchText)
        {
            var cleanSearch =
                (searchText ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return string.IsNullOrWhiteSpace(cleanSearch)
                ? "New Site"
                : cleanSearch;
        }

        private static string ResolveLimitedDashboardKind(string siteId)
        {
            var normalized =
                (siteId ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            if (normalized.EndsWith(
                    "MR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.AmsMr;
            }

            if (normalized.StartsWith(
                    "RX",
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(
                    "RE",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.Rx;
            }

            if (normalized.StartsWith(
                    "G",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.Igsd;
            }

            /*
             * DACS sites commonly use numeric IDs.
             * During Limited Mode, a numeric-only search is therefore
             * treated as DACS. AMS searches must include the MR suffix.
             */
            return SiteDashboardKinds.Dacs;
        }

        private static void ApplyBlankDashboardToSession(SiteDashboardTabSession session, string siteId)
        {
            ResetSessionForNewSiteLoad(session);

            session.DashboardKind = ResolveLimitedDashboardKind(siteId);

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
        private async Task LoadAsync(
            string rawSiteId,
            bool runAutoQuickTest = true,
            bool useSiteLoadOverlay = true)
        {
            /*
             * Tower pings still belong to the shared WorkspaceView.
             * Leave this in place until tower pings receive the same
             * per-dashboard-session treatment as network pings.
             */
            WorkspaceView.StopTowerPings();

            var siteId =
                (rawSiteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                TopBarView.StatusText =
                    "Enter a site ID first.";

                return;
            }

            var siteSearchCandidates =
                BuildSiteDashboardSearchCandidates(siteId)
                    .ToList();

            var existingSession =
                _sessions.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.HeaderText) &&
                    !x.HeaderText.StartsWith(
                        "Blank",
                        StringComparison.OrdinalIgnoreCase) &&
                    siteSearchCandidates.Any(candidate =>
                        string.Equals(
                            x.HeaderText,
                            candidate,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            x.SearchText,
                            candidate,
                            StringComparison.OrdinalIgnoreCase)));

            if (existingSession is not null)
            {
                /*
                 * Save the currently visible dashboard state before changing tabs.
                 * NetworkPingState now contains the live per-tab ping processes.
                 */
                SaveCurrentTabUiState();

                _selectedSessionKey =
                    existingSession.SessionKey;

                RenderSelectedSession();

                await LoadSiteNotesForSessionAsync(
                    existingSession,
                    CancellationToken.None);

                TopBarView.StatusText =
                    $"Switched to {existingSession.HeaderText}.";

                return;
            }

            var selectedSession =
                GetSelectedSession();

            if (selectedSession is null)
            {
                CreateBlankTab(
                    selectNewTab: true);

                selectedSession =
                    GetSelectedSession();
            }

            if (selectedSession is null)
                return;

            var previousLoadedSite =
                (selectedSession.SearchText ?? string.Empty).Trim();

            var isBlankSessionLoad =
                string.IsNullOrWhiteSpace(previousLoadedSite) ||
                previousLoadedSite.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase);

            var isDifferentSiteLoad =
                !isBlankSessionLoad &&
                !string.Equals(
                    previousLoadedSite,
                    siteId,
                    StringComparison.OrdinalIgnoreCase);

            var shouldClearForSiteLoad =
                isBlankSessionLoad ||
                isDifferentSiteLoad;

            if (shouldClearForSiteLoad)
            {
                /*
                 * This dashboard tab is being reused for another site.
                 * Stop only the network pings owned by this tab.
                 *
                 * Pings running in other dashboard tabs remain active.
                 */
                NetworkView.StopPingSession(
                    selectedSession.NetworkPingState);

                ResetSessionForNewSiteLoad(
                    selectedSession);
            }

            _loadCts?.Cancel();
            _loadCts?.Dispose();

            _loadCts =
                new CancellationTokenSource();

            try
            {
                if (useSiteLoadOverlay)
                {
                    ShowSiteLoadOverlay(
                        $"Loading {siteId}...");
                }

                TopBarView.SetLoading(true);

                TopBarView.StatusText =
                    $"Loading {siteId}...";

                UpdateSiteLoadOverlayMessage(
                    $"Loading dashboard data for {siteId}...");

                var dashboard =
                    await GetSiteOrTowerDashboardAsync(
                        siteId,
                        _loadCts.Token);

                var loadedSiteId =
                    GetObjectPropertyText(
                        dashboard,
                        "SiteId")
                    ?? siteId;

                selectedSession.TicketInfoText =
                    "Loading ticket data...";

                UpdateSiteLoadOverlayMessage(
                    $"Loading ticket data for {siteId}...");

                ApplyDashboardToSession(
                    selectedSession,
                    dashboard,
                    loadedSiteId);

                await ApplyPingScreenPortalUrlAsync(
                    selectedSession,
                    dashboard,
                    _loadCts.Token);

                UpdateSiteLoadOverlayMessage(
                    $"Loading site history for {loadedSiteId}...");

                await LoadSiteHistoryForSessionAsync(
                    selectedSession,
                    _loadCts.Token);

                if (shouldClearForSiteLoad)
                {
                    selectedSession.SelectedWorkspaceTabKey =
                        "TopWriteUp";
                }

                selectedSession.SnmpTargetIp =
                    shouldClearForSiteLoad ||
                    string.IsNullOrWhiteSpace(
                        selectedSession.SnmpTargetIp)
                        ? selectedSession.PrimaryIp
                        : selectedSession.SnmpTargetIp;

                selectedSession.SnmpSupportMessage =
                    "Loading SNMP configuration...";

                _selectedSessionKey =
                    selectedSession.SessionKey;

                RenderSelectedSession();

                UpdateSiteLoadOverlayMessage(
                    $"Refreshing tickets for {loadedSiteId}...");

                await RefreshTicketInfoAsync(
                    selectedSession,
                    _loadCts.Token);

                if (ShouldLoadSnmpForDashboard(
                        selectedSession))
                {
                    UpdateSiteLoadOverlayMessage(
                        $"Loading SNMP configuration for {loadedSiteId}...");

                    await RefreshSnmpConfigAsync(
                        selectedSession,
                        _loadCts.Token);
                }
                else
                {
                    ClearSnmpForUnsupportedDashboard(
                        selectedSession);
                }

                if (selectedSession.SessionKey ==
                    _selectedSessionKey)
                {
                    RenderSelectedSession();
                }

                if (runAutoQuickTest &&
                    shouldClearForSiteLoad &&
                    ShouldRunNetworkAutoQuickTest(
                        selectedSession))
                {
                    await Dispatcher.InvokeAsync(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.Loaded);

                    UpdateSiteLoadOverlayMessage(
                        $"Running quick network test for {loadedSiteId}...");

                    await NetworkView
                        .RunQuickReachabilityTestForAllAsync();

                    selectedSession.NetworkPingState =
                        NetworkView.GetPingSessionState();
                }

                if (dashboard?.IsCached == true)
                {
                    var cachedAtText =
                        dashboard.CachedAtUtc.HasValue
                            ? dashboard.CachedAtUtc.Value
                                .ToLocalTime()
                                .ToString(
                                    "MMM d, yyyy h:mm tt")
                            : "an earlier synchronization";

                    TopBarView.StatusText =
                        $"Loaded {loadedSiteId} using cached Parent DB data from {cachedAtText}.";
                }
                else
                {
                    TopBarView.StatusText =
                        $"Loaded {loadedSiteId}.";
                }
            }
            catch (OperationCanceledException)
            {
                /*
                 * A newer dashboard load replaced this request.
                 */
            }
            catch (Exception ex)
                when (IsParentDatabaseUnavailableException(ex))
            {
                var limitedSiteId =
                    ResolveLimitedModeSiteId(siteId);

                TopBarView.StatusText =
                    "Parent database unavailable. " +
                    $"Opening Limited Mode for {limitedSiteId}...";

                /*
                 * Start with the existing blank-dashboard setup.
                 * This leaves all IP fields editable and keeps the
                 * write-up workspace available.
                 */
                ApplyBlankDashboardToSession(
                    selectedSession,
                    limitedSiteId);

                selectedSession.SiteStatusText =
                    "Limited Mode";

                selectedSession.TicketInfoText =
                    "Loading Smart Grid Suite ticket data...";

                selectedSession.SnmpSupportMessage =
                    "Limited Mode — enter an IP address manually, " +
                    "then select an SNMP profile.";

                _selectedSessionKey =
                    selectedSession.SessionKey;

                RenderSelectedSession();

                var limitedModeToken =
                    _loadCts?.Token
                    ?? CancellationToken.None;

                try
                {
                    await ApplyPingScreenPortalUrlAsync(
                        selectedSession,
                        dashboard: null,
                        ct: limitedModeToken);
                }
                catch
                {
                    selectedSession.ShowIgsdPortalTab =
                        false;

                    selectedSession.IgsdPortalUrl =
                        string.Empty;
                }

                /*
                 * These features use the SmartGridSuite database,
                 * not the unavailable Parent DB.
                 */
                try
                {
                    await LoadSiteNotesForSessionAsync(
                        selectedSession,
                        limitedModeToken);
                }
                catch
                {
                    // Keep Limited Mode usable when notes cannot load.
                }

                try
                {
                    await LoadSiteHistoryForSessionAsync(
                        selectedSession,
                        limitedModeToken);
                }
                catch
                {
                    selectedSession.HistoryRows =
                        new List<SiteDashboardHistoryRowViewModel>();
                }

                try
                {
                    await RefreshTicketInfoAsync(
                        selectedSession,
                        limitedModeToken);
                }
                catch
                {
                    selectedSession.TicketInfoText =
                        "Ticket data is temporarily unavailable.";
                }

                try
                {
                    await RefreshSnmpConfigAsync(
                        selectedSession,
                        limitedModeToken);
                }
                catch
                {
                    selectedSession.SnmpSupported =
                        false;

                    selectedSession.SnmpSupportMessage =
                        "Limited Mode — enter an IP address manually " +
                        "to run ping diagnostics. " +
                        "SNMP configuration could not be loaded.";
                }

                if (selectedSession.SessionKey ==
                    _selectedSessionKey)
                {
                    RenderSelectedSession();
                }

                TopBarView.StatusText =
                    $"Limited dashboard ready for {limitedSiteId}. " +
                    "Parent site data is unavailable.";
            }
            catch (Exception ex)
                when (IsDashboardNotFoundException(ex))
            {
                var blankSiteId =
                    ResolveBlankDashboardSiteId(siteId);

                TopBarView.StatusText =
                    "No existing site found. " +
                    $"Opening blank dashboard for {blankSiteId}...";

                ApplyBlankDashboardToSession(
                    selectedSession,
                    blankSiteId);

                _selectedSessionKey =
                    selectedSession.SessionKey;

                RenderSelectedSession();

                await LoadSiteNotesForSessionAsync(
                    selectedSession,
                    _loadCts?.Token
                    ?? CancellationToken.None);

                try
                {
                    await LoadSiteHistoryForSessionAsync(
                        selectedSession,
                        _loadCts?.Token
                        ?? CancellationToken.None);
                }
                catch
                {
                    selectedSession.HistoryRows =
                        new List<SiteDashboardHistoryRowViewModel>();
                }

                /*
                 * Tickets and SNMP profiles can still load even when
                 * no matching Parent DB dashboard record exists.
                 */
                try
                {
                    await RefreshTicketInfoAsync(
                        selectedSession,
                        _loadCts?.Token
                        ?? CancellationToken.None);

                    await RefreshSnmpConfigAsync(
                        selectedSession,
                        _loadCts?.Token
                        ?? CancellationToken.None);

                    if (selectedSession.SessionKey ==
                        _selectedSessionKey)
                    {
                        RenderSelectedSession();
                    }
                }
                catch
                {
                    /*
                     * Keep the blank dashboard usable even if ticket
                     * or SNMP configuration loading fails.
                     */
                }

                TopBarView.StatusText =
                    $"Blank dashboard ready for {blankSiteId}.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText =
                    $"Load failed: {ex.Message}";
            }
            finally
            {
                TopBarView.SetLoading(false);

                if (useSiteLoadOverlay)
                {
                    HideSiteLoadOverlay();
                }
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
            session.SnmpProfile = null;
            session.SnmpOidResults = new Dictionary<ulong, string>();
        }

        private async Task LoadSiteHistoryForSessionAsync(SiteDashboardTabSession? session, CancellationToken ct = default)
        {
            if (session is null)
            {
                return;
            }

            var siteId =
                ResolveSiteNotesSiteId(session);

            if (string.IsNullOrWhiteSpace(siteId))
            {
                session.HistoryRows =
                    new List<SiteDashboardHistoryRowViewModel>();

                return;
            }

            var history =
                await _api.GetAsync<List<SiteHistoryPreviewDto>>(
                    $"api/tickets/site-history/{Uri.EscapeDataString(siteId)}",
                    ct)
                ?? new List<SiteHistoryPreviewDto>();

            /*
             * BuildHistoryRows already understands an object with a
             * History collection, matching the normal dashboard shape.
             */
            session.HistoryRows =
                BuildHistoryRows(
                    new
                    {
                        History = history
                    });
        }

        private async Task LoadSiteNotesForSessionAsync(SiteDashboardTabSession? session, CancellationToken ct = default)
        {
            if (session == null)
            {
                await WorkspaceView.LoadSiteNotesAsync(string.Empty, ct);
                return;
            }

            var siteId = ResolveSiteNotesSiteId(session);

            await WorkspaceView.LoadSiteNotesAsync(siteId, ct);
        }

        private static string ResolveSiteNotesSiteId(SiteDashboardTabSession session)
        {
            var header = (session.HeaderText ?? string.Empty).Trim();
            var search = (session.SearchText ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(header) &&
                !header.StartsWith("Blank", StringComparison.OrdinalIgnoreCase))
            {
                return header;
            }

            return search;
        }
    }
}