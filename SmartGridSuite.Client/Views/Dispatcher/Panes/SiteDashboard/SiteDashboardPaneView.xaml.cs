using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Administration.Technicians;
using SmartGridSuite.Contracts.Settings;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using SmartGridSuite.Contracts.Tickets;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using static SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard.SiteDashboardWorkspaceView;
using SmartGridSuite.Contracts.Crews;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView : UserControl
    {
        private readonly ApiClient _api;
        private readonly TicketsApi _ticketsApi;
        private CancellationTokenSource? _loadCts;

        private string _currentCnpTechName = string.Empty;

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

        //Choose which Dashboard to Render
        private void ApplyDashboardToSession(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            session.DashboardKind = dashboard?.DashboardKind ?? string.Empty;

            switch (session.DashboardKind)
            {
                case SiteDashboardKinds.AmsMr:
                    ApplyAmsMrDashboard(session, dashboard, requestedSiteId);
                    break;

                case SiteDashboardKinds.Igsd:
                    ApplyIgsdDashboard(session, dashboard, requestedSiteId);
                    break;

                case SiteDashboardKinds.Dacs:
                    ApplyDacsDashboard(session, dashboard, requestedSiteId);
                    break;

                case SiteDashboardKinds.Rx:
                    ApplyRxDashboard(session, dashboard, requestedSiteId);
                    break;

                case SiteDashboardKinds.Tower:
                    ApplyTowerDashboard(session, dashboard, requestedSiteId);
                    break;

                default:
                    ApplyFallbackDashboard(session, dashboard, requestedSiteId);
                    break;
            }

            session.SnmpPrimaryCommType = dashboard?.Route?.PrimaryCommType?.Trim() ?? string.Empty;
        }

        //AMS Adapter
        private void ApplyAmsMrDashboard(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            var dto = DeserializeDashboardData<AmsSiteDashboardDto>(dashboard);

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = "";
            session.RangeExtenderLinkUrl = string.Empty;

            session.HeaderText = dto?.SiteId ?? requestedSiteId;
            session.SearchText = session.HeaderText;

            session.AddressText = BuildAddress(
                dto?.StreetNo,
                dto?.StreetName,
                dto?.City,
                dto?.StateCode,
                dto?.ZipCode);

            session.CoordinatesText = BuildCoordinates(dto?.Latitude, dto?.Longitude);

            session.PrimaryIp = DashIfEmpty(dto?.PrimaryCommsIp);
            session.LanIp = DashIfEmpty(dto?.SecondaryLanIp);
            session.SecondaryIp = DashIfEmpty(dto?.SecondaryWanIp);
            
            //Just to clear it so it doesn't bleed across Dashboards
            session.TopTunnelIp = "—";

            session.SiteStatusText = dto?.SiteStatus ?? string.Empty;
            session.TopAccessTitleText = BuildTopAccessTitle(dashboard);

            session.TopInfoText = BuildTopInfoSummary(dashboard);
            session.EquipmentText = BuildEquipmentSummary(dashboard);
            session.HistoryRows = BuildHistoryRows(dashboard);
        }

        //------//
        //-IGSD-//
        //------//

        //Adapter
        private void ApplyIgsdDashboard(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            var dto = DeserializeDashboardData<IgsdSiteDashboardDto>(dashboard);

            session.RangeExtenderLinkUrl = string.Empty;

            session.DashboardKind = SiteDashboardKinds.Igsd;

            session.HeaderText = dto?.SiteId ?? requestedSiteId;
            session.SearchText = session.HeaderText;

            session.AddressText = BuildAddress(
                dto?.StreetNo,
                dto?.StreetName,
                dto?.City,
                dto?.StateCode,
                dto?.ZipCode);

            session.CoordinatesText = BuildCoordinates(dto?.Latitude, dto?.Longitude);

            var primaryType = (dto?.PrimaryCommType ?? string.Empty).Trim();
            var secondaryType = (dto?.SecondaryCommType ?? string.Empty).Trim();

            var isPrimaryLte = primaryType.Equals("LTE", StringComparison.OrdinalIgnoreCase);
            var isSecondaryLte = secondaryType.Equals("LTE", StringComparison.OrdinalIgnoreCase);
            var isDualCell = isPrimaryLte && isSecondaryLte;

            // Primary ping target:
            // RF700 primary -> radio IP
            // LTE primary  -> WAN IP
            session.PrimaryIp = isPrimaryLte
                ? DashIfEmpty(dto?.PrimaryWanIp ?? dto?.PrimaryLanIp)
                : DashIfEmpty(dto?.PrimaryCommsIp ?? dto?.PrimaryWanIp);

            // Secondary ping target stays WAN for LTE secondary
            session.SecondaryIp = DashIfEmpty(dto?.SecondaryWanIp ?? dto?.SecondaryLanIp);

            // Not used for IG layout
            session.LanIp = string.Empty;

            // Primary reference card
            session.IgsdPrimaryCommsEthernetIp = isPrimaryLte
                ? (dto?.PrimaryLanIp?.Trim() ?? string.Empty)
                : string.Empty; 

            session.IgsdPrimaryRtuIp = DashIfEmpty(dto?.PrimaryRtuIp);

            // Secondary reference card
            session.IgsdSecondaryCommsEthernetIp = DashIfEmpty(dto?.SecondaryLanIp);
            session.IgsdSecondaryRtuIp = DashIfEmpty(dto?.SecondaryRtuIp);

            // Keep the raw tunnel if you want it for future logic
            session.IgsdPrimaryTunnelIp = DashIfEmpty(dto?.PrimaryTunnelIp);

            // But hide the TOP tunnel row on dual-cell sites
            session.TopTunnelIp = isDualCell
                ? "—"
                : session.IgsdPrimaryTunnelIp;

            session.SiteStatusText = dto?.SiteStatus ?? string.Empty;
            session.TopAccessTitleText = BuildTopAccessTitle(dashboard);
            session.TopInfoText = BuildTopInfoSummary(dashboard);
            session.EquipmentText = BuildEquipmentSummary(dashboard);
            session.HistoryRows = BuildHistoryRows(dashboard);

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = string.Empty;
        }
        //Portal Helper
        private async Task ApplyPingScreenPortalUrlAsync(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, CancellationToken ct)
        {
            var dashboardKind = dashboard?.DashboardKind ?? string.Empty;

            var showPingScreen =
                string.Equals(dashboardKind, SiteDashboardKinds.Igsd, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dashboardKind, SiteDashboardKinds.Dacs, StringComparison.OrdinalIgnoreCase);

            if (!showPingScreen)
            {
                session.ShowIgsdPortalTab = false;
                session.IgsdPortalUrl = string.Empty;
                return;
            }

            var dto = await _api.GetIgsdPortalUrlAsync(ct);
            var url = (dto?.Url ?? string.Empty).Trim();

            session.ShowIgsdPortalTab = !string.IsNullOrWhiteSpace(url);
            session.IgsdPortalUrl = url;
        }

        //------//
        //-DACs-//
        //------//
        private void ApplyDacsDashboard(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            var dto = DeserializeDashboardData<DacsSiteDashboardDto>(dashboard);

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = "";
            session.RangeExtenderLinkUrl = string.Empty;

            session.HeaderText = dto?.SiteId ?? requestedSiteId;
            session.SearchText = session.HeaderText;

            session.AddressText = BuildAddress(
                dto?.StreetNo,
                dto?.StreetName,
                dto?.City,
                dto?.StateCode,
                dto?.ZipCode);

            session.CoordinatesText = BuildCoordinates(dto?.Latitude, dto?.Longitude);

            session.PrimaryIp = DashIfEmpty(dto?.PrimaryCommsIp);
            session.LanIp = DashIfEmpty(dto?.TunnelIp);
            session.SecondaryIp = DashIfEmpty(dto?.RtuIp);

            //Just to clear it so it doesn't bleed across Dashboards
            session.TopTunnelIp = "—";

            session.SiteStatusText = dto?.SiteStatus ?? string.Empty;
            session.TopAccessTitleText = BuildTopAccessTitle(dashboard);

            session.TopInfoText = BuildTopInfoSummary(dashboard);
            session.EquipmentText = BuildEquipmentSummary(dashboard);
            session.HistoryRows = BuildHistoryRows(dashboard);
        }

        //------//
        //--RX--//
        //------//
        private void ApplyRxDashboard(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            var dto = DeserializeDashboardData<RxSiteDashboardDto>(dashboard);

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = "";
            session.RangeExtenderLinkUrl = _rangeExtenderLinkUrl;

            session.HeaderText = dto?.SiteId ?? requestedSiteId;
            session.SearchText = session.HeaderText;

            session.AddressText = BuildAddress(
                dto?.StreetNo,
                dto?.StreetName,
                dto?.City,
                dto?.StateCode,
                dto?.ZipCode);

            session.CoordinatesText = BuildCoordinates(dto?.Latitude, dto?.Longitude);

            // RX does not really fit the same 3-IP model yet, so leave these blank for now.
            session.PrimaryIp = "—";
            session.LanIp = "—";
            session.SecondaryIp = "—";

            //Just to clear it so it doesn't bleed across Dashboards
            session.TopTunnelIp = "—";

            session.SiteStatusText = dto?.SiteStatus ?? string.Empty;
            session.TopAccessTitleText = "Range Extender";

            session.TopInfoText =
                $"Range Extender SN: {DashIfEmpty(dto?.MeterNumber)}{Environment.NewLine}"+
                $"MAC Address: {DashIfEmpty(dto?.MacAddress)}{Environment.NewLine}" +
                $"Pole Point: {DashIfEmpty(dto?.PolePoint)}{Environment.NewLine}" +
                $"Transformer GLN: {DashIfEmpty(dto?.TransformerGln)}";

            session.EquipmentText =
                $"Range Extender SN: {DashIfEmpty(dto?.MeterNumber)}";

            session.HistoryRows = BuildHistoryRows(dashboard);

            if (string.IsNullOrWhiteSpace(session.SelectedWorkspaceTabKey) ||
                string.Equals(session.SelectedWorkspaceTabKey, "TopWriteUp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(session.SelectedWorkspaceTabKey, "SNMPTool", StringComparison.OrdinalIgnoreCase))
            {
                session.SelectedWorkspaceTabKey = "RxOverview";
            }

            if (string.IsNullOrWhiteSpace(session.SelectedWorkspaceTabKey) ||
                string.Equals(session.SelectedWorkspaceTabKey, "TopWriteUp", StringComparison.OrdinalIgnoreCase))
            {
                session.SelectedWorkspaceTabKey = "RxOverview";
            }
        }

        //RX Portal Helper
        private async Task LoadRangeExtenderLinkUrlForWorkspaceAsync()
        {
            try
            {
                var dto = await _api.GetRangeExtenderLinkUrlAsync();
                _rangeExtenderLinkUrl = dto?.Url?.Trim() ?? string.Empty;

                foreach (var session in _sessions.Where(x =>
                             string.Equals(x.DashboardKind, SiteDashboardKinds.Rx, StringComparison.OrdinalIgnoreCase)))
                {
                    session.RangeExtenderLinkUrl = _rangeExtenderLinkUrl;
                }

                var selected = GetSelectedSession();

                if (selected is not null &&
                    string.Equals(selected.DashboardKind, SiteDashboardKinds.Rx, StringComparison.OrdinalIgnoreCase))
                {
                    RenderSelectedSession();
                }
            }
            catch
            {
                _rangeExtenderLinkUrl = string.Empty;
            }
        }

        //-------//
        //-Tower-//
        //-------//
        private void ApplyTowerDashboard(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            var dto = DeserializeDashboardData<TowerDashboardDto>(dashboard);

            session.DashboardKind = SiteDashboardKinds.Tower;

            session.TowerTopNameId = dto?.TopNameId;
            session.HeaderText = dto?.TopName?.Replace("_", "-") ?? requestedSiteId;
            session.SearchText = session.HeaderText;

            session.AddressText = DashIfEmpty(dto?.FullAddress);
            session.CoordinatesText = BuildCoordinates(dto?.Latitude, dto?.Longitude);

            session.PrimaryIp = "—";
            session.LanIp = "—";
            session.SecondaryIp = "—";
            session.TopTunnelIp = "—";

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = string.Empty;
            session.RangeExtenderLinkUrl = string.Empty;

            session.SiteStatusText = "Tower";
            session.TopAccessTitleText = dto?.TopName?.Replace("_", "-") ?? "Tower";
            session.TopInfoText = string.Empty;
            session.EquipmentText = string.Empty;

            session.TowerSummaryText = BuildTowerSummary(dto);
            session.TowerSectors = dto?.Sectors ?? new List<TowerSectorDto>();

            session.HistoryRows = BuildHistoryRows(dashboard);

            session.SelectedWorkspaceTabKey = "TowerOverview";
        }
        private static string BuildTowerSummary(TowerDashboardDto? dto)
        {
            if (dto is null)
                return string.Empty;

            var lines = new List<string>();

            AddLine(lines, "Top Name", dto.TopName?.Replace("_", "-"));
            AddLine(lines, "Description", dto.TopDescription);
            AddLine(lines, "Top Type", dto.TopType);
            AddLine(lines, "IP Assignment", dto.IpAssignment);
            AddLine(lines, "GPS ID", dto.GpsId?.ToString());
            AddLine(lines, "Address", dto.FullAddress);

            if (dto.Latitude.HasValue || dto.Longitude.HasValue)
                AddLine(lines, "Coordinates", $"{dto.Latitude}, {dto.Longitude}");

            AddLine(lines, "Customer Owned", dto.CustomerOwned.HasValue
                ? dto.CustomerOwned.Value ? "Yes" : "No"
                : null);

            AddLine(lines, "Note", dto.Note);

            return string.Join(Environment.NewLine, lines);
        }

        //Fallback Adapter
        private void ApplyFallbackDashboard(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard, string requestedSiteId)
        {
            session.HeaderText = GetObjectPropertyText(dashboard, "SiteId") ?? requestedSiteId;
            session.SearchText = session.HeaderText;
            session.AddressText = BuildFullAddress(dashboard) ?? "—";
            session.CoordinatesText = BuildCoordinateSummary(dashboard) ?? "—";

            session.ShowIgsdPortalTab = false;
            session.IgsdPortalUrl = "";
            session.RangeExtenderLinkUrl = string.Empty;

            session.PrimaryIp = GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommunicationsIp",
                "PrimaryCommIp",
                "PrimaryCommsIp",
                "PrimaryCommsIP",
                "RadioIP",
                "RadioIp") ?? "—";

            session.LanIp = GetDashboardDataFieldText(
                dashboard,
                "SecondaryLanIp",
                "SecondaryLanIP",
                "EthernetIP",
                "EthernetIp") ?? "—";

            session.SecondaryIp = GetDashboardDataFieldText(
                dashboard,
                "SecondaryWanIp",
                "SecondaryWanIP",
                "SecondaryCommunicationsIp",
                "SecondaryCommsIp",
                "CellularIP",
                "CellularIp",
                "IP1",
                "WanIp") ?? "—";

            //Just to clear it so it doesn't bleed across Dashboards
            session.TopTunnelIp = "—";

            session.TopInfoText = BuildTopInfoSummary(dashboard);
            session.SiteStatusText = GetDashboardDataFieldText(dashboard, "SiteStatus", "Status") ?? string.Empty;
            session.TopAccessTitleText = BuildTopAccessTitle(dashboard);
            session.EquipmentText = BuildEquipmentSummary(dashboard);
            session.HistoryRows = BuildHistoryRows(dashboard);
        }
        

        private async void WorkspaceView_RefreshSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            try
            {
                TopBarView.StatusText = "Refreshing SNMP configuration...";
                await RefreshSnmpConfigAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSnmpOnly(session);

                TopBarView.StatusText = "SNMP configuration refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP configuration refresh failed: {ex.Message}";
            }
        }

        private void WorkspaceView_SnmpTargetChanged(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            session.SnmpTargetIp = WorkspaceView.GetSnmpTargetIp();
        }

        private async void WorkspaceView_SetSelectedSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var selectedOid = WorkspaceView.GetSelectedWritableSnmpOid();
            if (selectedOid is null)
            {
                TopBarView.StatusText = "Select a writable SNMP OID first.";
                return;
            }

            if (!selectedOid.IsWritable)
            {
                TopBarView.StatusText = "Selected OID is read-only.";
                return;
            }

            if (!session.SnmpProfileId.HasValue || session.SnmpProfileId.Value == 0)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            var setValue = WorkspaceView.GetSnmpSetValue();
            if (string.IsNullOrWhiteSpace(setValue))
            {
                TopBarView.StatusText = "Enter a value to set.";
                return;
            }

            try
            {
                TopBarView.StatusText = $"Setting {selectedOid.Label}...";

                var result = await _api.PostAsync<SnmpSetSelectedRequestDto, SnmpSetResultDto>(
                    "api/snmp-profiles/set-selected",
                    new SnmpSetSelectedRequestDto
                    {
                        ProfileId = session.SnmpProfileId.Value,
                        OidId = selectedOid.Id,
                        TargetIp = targetIp,
                        Value = setValue
                    });

                session.SnmpTargetIp = targetIp;
                WorkspaceView.ShowSnmpSetResult(result);

                TopBarView.StatusText = result?.Success == true
                    ? $"SNMP set returned {result.DisplayValue}."
                    : $"SNMP set failed: {result?.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP set failed: {ex.Message}";
                WorkspaceView.ShowSnmpSetResult(new SnmpSetResultDto
                {
                    Success = false,
                    TargetIp = targetIp,
                    RequestedValue = setValue,
                    Label = selectedOid.Label,
                    Oid = selectedOid.Oid,
                    DecodeMode = selectedOid.DecodeMode,
                    ErrorMessage = ex.Message
                });
            }
        }

        private async Task RefreshSnmpConfigAsync(SiteDashboardTabSession session, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(session.SnmpTargetIp) || session.SnmpTargetIp == "—")
                session.SnmpTargetIp = GetDefaultSnmpTargetIp(session);

            session.SnmpSupported = false;
            session.SnmpDeviceFamily = string.Empty;
            session.SnmpProfileName = string.Empty;
            session.SnmpProfileId = null;
            session.SnmpSupportMessage = string.Empty;
            session.SnmpProfiles = new List<SnmpProfileListItemDto>();
            session.SnmpOids = new List<SnmpOidConfigDto>();
            session.SnmpOidResults = new Dictionary<ulong, string>();

            var allProfiles = await _api.GetAsync<List<SnmpProfileListItemDto>>(
                "api/snmp-profiles",
                ct) ?? new List<SnmpProfileListItemDto>();

            session.SnmpProfiles = allProfiles
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .ToList();

            if (session.SnmpProfiles.Count == 0)
            {
                session.SnmpSupportMessage = "No active SNMP profiles are configured.";
                return;
            }

            session.SnmpSupported = true;

            ulong selectedProfileId;

            if (session.SnmpProfileId.HasValue &&
                session.SnmpProfiles.Any(x => x.Id == session.SnmpProfileId.Value))
            {
                selectedProfileId = session.SnmpProfileId.Value;
            }
            else if (string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase))
            {
                selectedProfileId =
                    session.SnmpProfiles.FirstOrDefault(x =>
                        x.Name.Contains("Tower", StringComparison.OrdinalIgnoreCase))?.Id
                    ?? session.SnmpProfiles.First().Id;
            }
            else
            {
                selectedProfileId = session.SnmpProfiles.First().Id;
            }

            await LoadSnmpProfileIntoSessionAsync(session, selectedProfileId, ct);
        }

        private static string GetDefaultSnmpTargetIp(SiteDashboardTabSession session)
        {
            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase))
            {
                var towerTarget = session.TowerSectors
                    .SelectMany(x => new[] { x.IPa, x.IPb })
                    .Select(x => (x ?? string.Empty).Trim())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "—");

                return towerTarget ?? string.Empty;
            }

            return new[]
                {
            session.PrimaryIp,
            session.LanIp,
            session.SecondaryIp
        }
                .Select(x => (x ?? string.Empty).Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "—")
                ?? string.Empty;
        }

        private static IEnumerable<(string Key, string Label, string IpAddress)> BuildTowerSnmpTargets(SiteDashboardTabSession session)
        {
            var sectors = session.TowerSectors
                .OrderBy(x => GetTowerSectorSortRankForSnmp(x.Sector))
                .ThenBy(x => x.Sector)
                .ThenBy(x => x.TopSiteId)
                .ToList();

            foreach (var sector in sectors)
            {
                var sectorName = string.IsNullOrWhiteSpace(sector.Sector)
                    ? $"Sector {sector.TopSiteId}"
                    : sector.Sector.Trim();

                var ipA = (sector.IPa ?? string.Empty).Trim();
                var ipB = (sector.IPb ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(ipA) && ipA != "—")
                    yield return ($"{sectorName}-IPA", $"Sector {sectorName} IP A", ipA);

                if (!string.IsNullOrWhiteSpace(ipB) && ipB != "—")
                    yield return ($"{sectorName}-IPB", $"Sector {sectorName} IP B", ipB);
            }
        }

        private static int GetTowerSectorSortRankForSnmp(string? sector)
        {
            var value = (sector ?? string.Empty).Trim().ToUpperInvariant();

            if (value == "AP1")
                return 1;

            if (value == "AP2")
                return 2;

            if (value == "AP3")
                return 3;

            if (value.StartsWith("AP") &&
                int.TryParse(value[2..], out var apNumber))
            {
                return 100 + apNumber;
            }

            return 1000;
        }

        private async Task LoadSnmpProfileIntoSessionAsync(SiteDashboardTabSession session, ulong profileId, CancellationToken ct)
        {
            var profile = await _api.GetAsync<SnmpProfileDetailDto>(
                $"api/snmp-profiles/{profileId}",
                ct);

            if (profile is null)
            {
                session.SnmpSupported = false;
                session.SnmpProfileId = null;
                session.SnmpProfileName = string.Empty;
                session.SnmpOids = new List<SnmpOidConfigDto>();
                session.SnmpSupportMessage = "Selected SNMP profile could not be loaded.";
                return;
            }

            session.SnmpSupported = true;
            session.SnmpProfileId = profile.Id;
            session.SnmpProfileName = profile.Name;
            session.SnmpOids = profile.Oids
                .Where(x => x.ShowInWorkspace)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .ToList();

            session.SnmpSupportMessage = string.IsNullOrWhiteSpace(session.SnmpProfileName)
                ? $"SNMP ready for {session.HeaderText}."
                : $"SNMP ready for {session.HeaderText}. Profile: {session.SnmpProfileName}.";

            session.SnmpOidResults = new Dictionary<ulong, string>();
        }        

        private async void WorkspaceView_SelectedSnmpProfileChanged(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var selectedProfileId = WorkspaceView.GetSelectedSnmpProfileId();
            if (!selectedProfileId.HasValue || selectedProfileId.Value == 0)
                return;

            try
            {
                TopBarView.StatusText = "Loading SNMP profile...";
                await LoadSnmpProfileIntoSessionAsync(session, selectedProfileId.Value, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSnmpOnly(session);

                TopBarView.StatusText = $"Loaded SNMP profile {session.SnmpProfileName}.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP profile load failed: {ex.Message}";
            }
        }

        private async void TopBarView_LoadRequested(object? sender, EventArgs e)
        {
            await LoadAsync(TopBarView.SearchText);
        }

        private void TopBarView_AddTabRequested(object? sender, EventArgs e)
        {
            CreateBlankTab(selectNewTab: true);
            RenderSelectedSession();
        }

        private void TopBarView_SelectedTabChanged(object? sender, string? sessionKey)
        {
            _selectedSessionKey = sessionKey;
            RenderSelectedSession();
        }

        private void TopBarView_CloseTabRequested(object? sender, string? sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return;

            var index = _sessions.FindIndex(x => x.SessionKey == sessionKey);
            if (index < 0)
                return;

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

        private void WorkspaceView_WriteUpTextChanged(object? sender, string text)
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();
            if (session is null)
                return;

            session.WriteUpText = text ?? string.Empty;
        }

        private async void WorkspaceView_WriteUpSubmitRequested(object? sender, WriteUpSubmitRequestedEventArgs e)
        {
            if (_writeUpSubmitInProgress)
            {
                TopBarView.StatusText = "Write-up submit already running...";
                return;
            }

            var session = GetSelectedSession();

            if (session is null)
                return;

            try
            {
                _writeUpSubmitInProgress = true;
                TopBarView.StatusText = "Submitting write-up...";

                var targetTicketId = session.CurrentTicketId;

                if (targetTicketId <= 0)
                {
                    targetTicketId = await _ticketsApi.RequestTicketAsync(
                        session.HeaderText,
                        "Write-up submitted from Site Dashboard with no associated ticket.",
                        requestedBy: Environment.UserName,
                        CancellationToken.None);

                    session.CurrentTicketId = targetTicketId;
                }

                if (targetTicketId <= 0)
                {
                    TopBarView.StatusText = "Write-up submit failed: no ticket could be created or found.";
                    return;
                }

                await _ticketsApi.SubmitWriteUpAsync(
                    targetTicketId,
                    e.FinalWriteUpText,
                    submittedBy: Environment.UserName,
                    CancellationToken.None);

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Write-up submitted to ticket.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Write-up submit failed: {ex.Message}";
            }
            finally
            {
                _writeUpSubmitInProgress = false;
            }
        }

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

        private async void WorkspaceView_RefreshTicketRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            try
            {
                TopBarView.StatusText = "Refreshing ticket...";
                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Ticket refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Ticket refresh failed: {ex.Message}";
            }
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

            var existingSession = _sessions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.HeaderText, siteId, StringComparison.OrdinalIgnoreCase));

            if (existingSession is not null)
            {
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

            var isDifferentSiteLoad =
                !string.IsNullOrWhiteSpace(previousLoadedSite) &&
                !previousLoadedSite.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(previousLoadedSite, siteId, StringComparison.OrdinalIgnoreCase);

            if (isDifferentSiteLoad)
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

                if (isDifferentSiteLoad)
                {
                    selectedSession.SelectedWorkspaceTabKey = "TopWriteUp";
                }

                selectedSession.SnmpTargetIp =
                    isDifferentSiteLoad || string.IsNullOrWhiteSpace(selectedSession.SnmpTargetIp)
                        ? selectedSession.PrimaryIp
                        : selectedSession.SnmpTargetIp;

                selectedSession.SnmpSupportMessage = "Loading SNMP configuration...";

                _selectedSessionKey = selectedSession.SessionKey;
                RenderSelectedSession();

                await RefreshTicketInfoAsync(selectedSession, _loadCts.Token);
                await RefreshSnmpConfigAsync(selectedSession, _loadCts.Token);

                if (selectedSession.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                if (!string.Equals(selectedSession.DashboardKind, SiteDashboardKinds.Rx, StringComparison.OrdinalIgnoreCase))
                    _ = NetworkView.RunQuickReachabilityTestForAllAsync();
                TopBarView.StatusText = $"Loaded {loadedSiteId}.";
            }
            catch (OperationCanceledException)
            {
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


        //RENDER METHOD
        private void RenderSelectedSession()
        {
            WorkspaceView.StopTowerPings();
            EnsureInitialBlankTab();

            TopBarView.SetTabs(_sessions, _selectedSessionKey);

            var session = GetSelectedSession();
            if (session is null)
            {
                _renderingSession = true;
                try
                {
                    TopBarView.ResetHeader();
                    NetworkView.Reset();
                    WorkspaceView.Reset();
                }
                finally
                {
                    _renderingSession = false;
                }

                return;
            }

            WorkspaceView.CurrentCnpTechName = _currentCnpTechName;

            ApplyShellLayoutMode(session);
            _renderingSession = true;

            try
            {
                //Header area
                TopBarView.SetSelectedTab(session.SessionKey);
                TopBarView.SearchText = session.SearchText;
                TopBarView.AddressText = session.AddressText;
                TopBarView.CoordinatesText = session.CoordinatesText;

                if (string.IsNullOrWhiteSpace(TopBarView.StatusText))
                    TopBarView.StatusText = "Ready.";

                //Networking
                NetworkView.Reset();
                ApplyNetworkLabels(session);
                NetworkView.SiteHeader = BuildNetworkHeader(session.SiteStatusText, session.HeaderText);

                var isIgsd = string.Equals(
                    session.DashboardKind,
                    SiteDashboardKinds.Igsd,
                    StringComparison.OrdinalIgnoreCase);

                NetworkView.IsIgsdMode = isIgsd;

                NetworkView.PrimaryIp = session.PrimaryIp;
                NetworkView.LanIp = session.LanIp;
                NetworkView.SecondaryIp = session.SecondaryIp;

                NetworkView.IgsdPrimaryRtuIp = session.IgsdPrimaryRtuIp;
                NetworkView.IgsdPrimaryCommsEthernetIp = session.IgsdPrimaryCommsEthernetIp;
                NetworkView.IgsdSecondaryCommsEthernetIp = session.IgsdSecondaryCommsEthernetIp;
                NetworkView.IgsdSecondaryRtuIp = session.IgsdSecondaryRtuIp;

                NetworkView.ApplyLayoutMode();

                //Main Workspace
                WorkspaceView.Reset();
                WorkspaceView.TopAccessTitle = session.TopAccessTitleText;
                WorkspaceView.TopInfoText = session.TopInfoText;

                WorkspaceView.TopTunnelIp = session.TopTunnelIp;

                WorkspaceView.CurrentTicketId = session.CurrentTicketId;
                WorkspaceView.TicketInfoText = session.TicketInfoText;
                WorkspaceView.WriteUpText = session.WriteUpText;

                WorkspaceView.ShowPortalTab = session.ShowIgsdPortalTab;
                WorkspaceView.PortalUrl = session.IgsdPortalUrl;
                WorkspaceView.RangeExtenderLinkUrl = session.RangeExtenderLinkUrl;

                WorkspaceView.EquipmentDashboardKind = session.DashboardKind;
                WorkspaceView.EquipmentText = session.EquipmentText;
                WorkspaceView.SetHistoryRows(session.HistoryRows);
                WorkspaceView.TowerSummaryText = session.TowerSummaryText;
                WorkspaceView.SetTowerSectors(session.TowerSectors);
                WorkspaceView.SetSelectedWorkspaceTab(session.SelectedWorkspaceTabKey);

                               

                if (session.ShowIgsdPortalTab &&
                    string.Equals(session.SelectedWorkspaceTabKey, "Portal", StringComparison.OrdinalIgnoreCase))
                {
                    _ = WorkspaceView.NavigatePortalAsync();
                }


                WorkspaceView.SetSnmpContext(
                    session.SnmpSupported,
                    session.SnmpSupportMessage,
                    session.SnmpDeviceFamily,
                    session.SnmpProfileName,
                    session.PrimaryIp,
                    session.LanIp,
                    session.SecondaryIp,
                    session.SnmpTargetIp);

                if (string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase))
                {
                    WorkspaceView.SetSnmpTargetOptions(
                        BuildTowerSnmpTargets(session),
                        session.SnmpTargetIp);
                }

                WorkspaceView.SetSnmpProfiles(session.SnmpProfiles, session.SnmpProfileId);

                WorkspaceView.SetSnmpOids(session.SnmpOids, session.SnmpOidResults);
                WorkspaceView.RefreshEquipmentDisplay();
            }
            finally
            {
                _renderingSession = false;
            }
        }

        private void ApplyShellLayoutMode(SiteDashboardTabSession session)
        {
            var hideNetwork =
                string.Equals(session.DashboardKind, SiteDashboardKinds.Rx, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase);

            NetworkView.Visibility = hideNetwork ? Visibility.Collapsed : Visibility.Visible;

            NetworkColumn.Width = hideNetwork
                ? new GridLength(0)
                : new GridLength(340);

            NetworkGapColumn.Width = hideNetwork
                ? new GridLength(0)
                : new GridLength(8);
        }

        private async Task<SiteDashboardResponseDto?> GetSiteOrTowerDashboardAsync(string searchText, CancellationToken ct)
        {
            try
            {
                return await _api.GetSiteDashboardAsync(searchText, ct);
            }
            catch (ApiClient.ApiException ex) when (ex.StatusCode == 404)
            {
                var tower = await TryFindTowerDashboardAsync(searchText, ct);

                if (tower is not null)
                    return tower;

                throw;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                var tower = await TryFindTowerDashboardAsync(searchText, ct);

                if (tower is not null)
                    return tower;

                throw;
            }
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

        private string BuildTicketInfoSummaryFromTickets(IEnumerable<TicketListItemDto>? tickets)
        {
            var ticket = SelectBestTicket(tickets);
            if (ticket is null)
                return "No ticket data returned yet.";

            var lines = new List<string>();

            AddLine(lines, "Notification Name", GetObjectPropertyText(
                ticket,
                "NotificationName",
                "NotificationText",
                "NotificationDescription"));

            AddLine(lines, "Notification #", GetObjectPropertyText(
                ticket,
                "Notification",
                "NotificationNumber",
                "NotificationId"));

            AddLine(lines, "Problem/Issue", GetObjectPropertyText(
                ticket,
                "Problem",
                "Issue"));

            AddLine(lines, "Work Order", GetObjectPropertyText(
                ticket,
                "CurrentWorkOrder",
                "WorkOrder",
                "WorkOrderNumber"));

            AddLine(lines, "Work Order Type", NormalizeTicketWorkOrderType(GetObjectPropertyText(
                ticket,
                "WorkOrderClass",
                "WorkOrderType")));

            AddLine(lines, "Assigned To", GetObjectPropertyText(
                ticket,
                "AssignedTech",
                "Tech"));

            AddLine(lines, "Date Created", FormatTicketCreatedDate(GetObjectPropertyText(
                ticket,
                "CreatedAt",
                "Created")));

            AddLine(lines, "Current Status", GetObjectPropertyText(
                ticket,
                "Status",
                "TicketStatus"));

            return lines.Count == 0
                ? "No ticket data returned yet."
                : string.Join(Environment.NewLine, lines);
        }

        private static string NormalizeTicketWorkOrderType(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.Equals(text, "Cap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Capital", StringComparison.OrdinalIgnoreCase))
                return "Capital";

            if (string.Equals(text, "Maint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Maintenance", StringComparison.OrdinalIgnoreCase))
                return "Maintenance";

            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        private static string FormatTicketCreatedDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            return DateTime.TryParse(value, out var dt)
                ? dt.ToString("MM-dd-yyyy")
                : value.Trim();
        }

        private TicketListItemDto? SelectBestTicket(IEnumerable<TicketListItemDto>? tickets)
        {
            return tickets?
                .OrderByDescending(GetTicketStatusRank)
                .ThenByDescending(t => GetTicketDate(t, "LastActivityAt", "LastActivity"))
                .ThenByDescending(t => GetTicketDate(t, "CreatedAt", "Created"))
                .FirstOrDefault();
        }

        private static int GetTicketStatusRank(TicketListItemDto ticket)
        {
            var status = (GetObjectPropertyText(ticket, "Status", "TicketStatus") ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(status))
                return 1;

            if (status.Equals("Assigned", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("In Progress", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Needs Review", StringComparison.OrdinalIgnoreCase))
                return 2;

            if (status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Canceled", StringComparison.OrdinalIgnoreCase))
                return 0;

            return 1;
        }

        private static DateTime GetTicketDate(TicketListItemDto ticket, params string[] propertyNames)
        {
            var text = GetObjectPropertyText(ticket, propertyNames);
            return DateTime.TryParse(text, out var dt) ? dt : DateTime.MinValue;
        }

        private async Task RefreshTicketInfoAsync(SiteDashboardTabSession session, CancellationToken ct)
        {
            var siteId = session.HeaderText;
            if (string.IsNullOrWhiteSpace(siteId))
            {
                session.CurrentTicketId = 0;
                session.TicketInfoText = "No ticket data returned yet.";
                return;
            }

            var tickets = await _ticketsApi.GetTicketsBySiteAsync(siteId, ct);
            var bestTicket = SelectBestTicket(tickets);

            session.CurrentTicketId = bestTicket?.Id ?? 0;
            session.TicketInfoText = BuildTicketInfoSummaryFromTickets(
                bestTicket is null ? Array.Empty<TicketListItemDto>() : new[] { bestTicket });
        }

        private List<SiteDashboardHistoryRowViewModel> BuildHistoryRows(object? dashboard)
        {
            var result = new List<SiteDashboardHistoryRowViewModel>();

            if (dashboard is null)
                return result;

            var historyItems = FindHistoryEnumerableRecursive(dashboard);
            if (historyItems is null)
                return result;

            foreach (var item in historyItems)
            {
                if (item is null)
                    continue;

                var rawDateText =
                    GetFirstNonEmptyText(item, "VisitDate", "SiteDate", "Date", "CreatedAt");

                var narrative =
                    GetFirstNonEmptyText(item, "Narrative", "Summary", "Notes", "SiteWork")
                    ?? string.Empty;

                var issue =
                    GetFirstNonEmptyText(item, "Issue", "SiteIssue", "Problem")
                    ?? ExtractIssueFromNarrative(narrative)
                    ?? "—";

                var tech1 =
                    GetFirstNonEmptyText(item, "PrimaryTech", "Tech1", "Technician1")
                    ?? "—";

                var tech2 =
                    GetFirstNonEmptyText(item, "SecondaryTech", "Tech2", "Technician2")
                    ?? "—";

                result.Add(new SiteDashboardHistoryRowViewModel(
                    FormatHistoryDate(rawDateText),
                    tech1,
                    tech2,
                    issue,
                    narrative));
            }

            return result;
        }        
        
        private string BuildTopInfoSummary(object? dashboard)
        {
            var lines = new List<string>();

            AddLine(lines, "Site Status", GetDashboardDataFieldText(dashboard, "SiteStatus", "Status"));
            AddLine(lines, "TOP VIP", GetDashboardDataFieldText(dashboard, "TopVip", "TopVIP"));
            AddLine(lines, "TOP IP A", GetDashboardDataFieldText(dashboard, "TopIpA", "TopIPA"));
            AddLine(lines, "TOP IP B", GetDashboardDataFieldText(dashboard, "TopIpB", "TopIPB"));

            return lines.Count == 0
                ? "No TOP fields were returned for this site yet."
                : string.Join(Environment.NewLine, lines);
        }

        private static string BuildNetworkHeader(string? siteStatus, string? siteId)
        {
            var cleanSiteId = string.IsNullOrWhiteSpace(siteId) ? "Site" : siteId.Trim();

            if (string.IsNullOrWhiteSpace(siteStatus))
                return $"Site {cleanSiteId}";

            return $"Site {cleanSiteId} - {siteStatus.Trim()}";
        }

        private string BuildTopAccessTitle(object? dashboard)
        {
            var topName = GetDashboardDataFieldText(dashboard, "TopName", "AssociatedTop", "Top") ?? string.Empty;
            var topDescription = GetDashboardDataFieldText(dashboard, "TopDescription", "TopDescr", "ProductionTop") ?? string.Empty;
            var topSector = GetDashboardDataFieldText(dashboard, "TopSector", "Sector") ?? string.Empty;

            var cleanTopName = topName.Replace("_", "-").Trim();
            var cleanSector = topSector.Trim();
            var cleanDescription = topDescription.Trim();

            var left = cleanTopName;

            if (!string.IsNullOrWhiteSpace(cleanSector))
                left = string.IsNullOrWhiteSpace(left) ? cleanSector : $"{left}-{cleanSector}";

            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(cleanDescription))
                return "TOP Access";

            if (string.IsNullOrWhiteSpace(cleanDescription))
                return left;

            if (string.IsNullOrWhiteSpace(left))
                return $"({cleanDescription})";

            return $"{left} ({cleanDescription})";
        }

        private string BuildEquipmentSummary(SiteDashboardResponseDto? dashboard)
        {
            var sb = new StringBuilder();

            static void AddLine(StringBuilder builder, string label, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                var trimmed = value.Trim();
                if (trimmed == "—" || trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    return;

                if (builder.Length > 0)
                    builder.AppendLine();

                builder.Append(label);
                builder.Append(": ");
                builder.Append(trimmed);
            }

            // Enclosure
            AddLine(sb, "Enclosure Model", GetDashboardDataFieldText(
                dashboard,
                "EnclosureModel"));

            AddLine(sb, "Enclosure SN", GetDashboardDataFieldText(
                dashboard,
                "EnclosureSerialNumber"));

            // Primary communications
            AddLine(sb, "Primary Type", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommType",
                "PrimaryCommunicationsType"));

            AddLine(sb, "Primary Model", GetDashboardDataFieldText(
                dashboard,
                "PrimaryModel"));

            AddLine(sb, "Primary SN", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommsIdentifier",
                "PrimaryCommunicationsIdentifier",
                "RadioSN",
                "RadioSn"));

            // Secondary communications
            AddLine(sb, "Secondary Type", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommType",
                "SecondaryCommunicationsType"));

            AddLine(sb, "Secondary Model", GetDashboardDataFieldText(
                dashboard,
                "SecondaryModel"));

            AddLine(sb, "Secondary SN", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommsIdentifier",
                "SecondaryCommunicationsIdentifier"));

            // Antenna
            AddLine(sb, "Antenna SN", GetDashboardDataFieldText(
                dashboard,
                "AntennaSerialNumber"));

            // Site Hardware / Access Hardware
            AddLine(sb, "Cyberlock SN", GetDashboardDataFieldText(
                dashboard,
                "CyberlockSerialNumber"));

            // Access & Security
            AddLine(sb, "Tunnel PSK", GetDashboardDataFieldText(
                dashboard,
                "TunnelPsk"));

            AddLine(sb, "Secondary WiFi SSID", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommsSsid",
                "SecondarySsid"));

            AddLine(sb, "Secondary WiFi Password", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommsPassword",
                "SecondaryPassword"));

            AddLine(sb, "Primary WiFi SSID", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommsSsid",
                "PrimarySsid"));

            AddLine(sb, "Primary WiFi Password", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommsPassword",
                "PrimaryPassword"));

            return sb.Length == 0 ? "—" : sb.ToString();
        }

        private static void AddLine(List<string> lines, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"{label}: {value}");
        }

        private static IEnumerable<object?>? FindHistoryEnumerableRecursive(object source)
        {
            var direct = FindHistoryEnumerableOnUnknown(source);
            if (direct is not null)
                return direct;

            var dataProp = source.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (dataProp is null)
                return null;

            var dataValue = dataProp.GetValue(source);
            return FindHistoryEnumerableOnUnknown(dataValue);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableOnUnknown(object? value)
        {
            if (value is null)
                return null;

            if (value is JsonElement json)
                return FindHistoryEnumerableInJson(json);

            return FindHistoryEnumerableOnObject(value);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableOnObject(object source)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);

                if (value is string || value is not IEnumerable enumerable)
                    continue;

                var elementType = GetEnumerableElementType(prop.PropertyType);

                if (elementType?.Name == "SiteHistoryPreviewDto" ||
                    prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase))
                {
                    return enumerable.Cast<object?>();
                }
            }

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);
                if (value is not JsonElement json)
                    continue;

                var nested = FindHistoryEnumerableInJson(json);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static IEnumerable<object?>? FindHistoryEnumerableInJson(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in json.EnumerateObject())
            {
                if (prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.Array)
                {
                    return prop.Value.EnumerateArray()
                        .Select(x => (object?)x.Clone())
                        .ToList();
                }
            }

            foreach (var prop in json.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var nested = FindHistoryEnumerableInJson(prop.Value);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                if (args.Length == 1)
                    return args[0];
            }

            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                     i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments().FirstOrDefault();
        }

        private static string? GetFirstNonEmptyText(object source, params string[] propertyNames)
        {
            if (source is JsonElement json)
                return FirstNonEmptyJsonProperty(json, propertyNames);

            return FirstNonEmptyObjectProperty(source, propertyNames);
        }

        private static string? FirstNonEmptyObjectProperty(object source, params string[] propertyNames)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var candidate in propertyNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                var prop = props.FirstOrDefault(p => NormalizeToken(p.Name) == normalizedCandidate);
                if (prop is null)
                    continue;

                var value = prop.GetValue(source);
                if (value is null)
                    continue;

                var text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private static string? FirstNonEmptyJsonProperty(JsonElement json, params string[] propertyNames)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in json.EnumerateObject())
            {
                var normalizedActual = NormalizeToken(prop.Name);

                foreach (var candidate in propertyNames)
                {
                    if (normalizedActual != NormalizeToken(candidate))
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private static object? GetDashboardDataValue(object? dashboard)
        {
            if (dashboard is null)
                return null;

            var dataProp = dashboard.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            return dataProp?.GetValue(dashboard);
        }        

        private static string? GetObjectPropertyText(object? source, params string[] propertyNames)
        {
            if (source is null)
                return null;

            return GetFirstNonEmptyText(source, propertyNames);
        }

        private string? GetDashboardDataFieldText(object? dashboard, params string[] candidateNames)
        {
            var data = GetDashboardDataValue(dashboard);
            if (data is null)
                return null;

            if (data is JsonElement json)
                return GetDashboardDataFieldTextFromJson(json, candidateNames);

            return GetDashboardDataFieldTextFromObject(data, candidateNames);
        }

        private string? GetDashboardDataFieldTextFromObject(object source, params string[] candidateNames)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var candidate in candidateNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                var prop = props.FirstOrDefault(p => NormalizeToken(p.Name) == normalizedCandidate);
                if (prop is null)
                    continue;

                var value = prop.GetValue(source);
                if (value is null)
                    continue;

                var text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private string? GetDashboardDataFieldTextFromJson(JsonElement json, params string[] candidateNames)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var candidate in candidateNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                foreach (var prop in json.EnumerateObject())
                {
                    if (NormalizeToken(prop.Name) != normalizedCandidate)
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private string? BuildFullAddress(object? dashboard)
        {
            var direct = GetDashboardDataFieldText(
                dashboard,
                "FullAddress",
                "Address",
                "StreetAddress",
                "FormattedAddress");

            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var streetNo = GetDashboardDataFieldText(dashboard, "StreetNo");
            var streetName = GetDashboardDataFieldText(dashboard, "StreetName");
            var city = GetDashboardDataFieldText(dashboard, "City");
            var state = GetDashboardDataFieldText(dashboard, "State", "StateCode");
            var zip = GetDashboardDataFieldText(dashboard, "Zip", "ZipCode");

            var line1 = string.Join(" ", new[] { streetNo, streetName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var cityState = string.Join(", ", new[] { city, state }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var line2 = string.Join(" ", new[] { cityState, zip }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var combined = string.Join("  ", new[] { line1, line2 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }

        private string? BuildCoordinateSummary(object? dashboard)
        {
            var latitude = GetDashboardDataFieldText(dashboard, "Latitude", "Lat");
            var longitude = GetDashboardDataFieldText(dashboard, "Longitude", "Lon", "Lng");

            if (string.IsNullOrWhiteSpace(latitude) && string.IsNullOrWhiteSpace(longitude))
                return null;

            if (string.IsNullOrWhiteSpace(latitude))
                return longitude;

            if (string.IsNullOrWhiteSpace(longitude))
                return latitude;

            return $"{latitude}, {longitude}";
        }

        private static string? ExtractIssueFromNarrative(string? narrative)
        {
            if (string.IsNullOrWhiteSpace(narrative))
                return null;

            var lines = narrative
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var line in lines)
            {
                if (line.StartsWith("Site Issue:", StringComparison.OrdinalIgnoreCase))
                    return line["Site Issue:".Length..].Trim();
            }

            return lines.FirstOrDefault();
        }

        private static string FormatHistoryDate(string? rawDateText)
        {
            if (string.IsNullOrWhiteSpace(rawDateText))
                return "—";

            if (DateTime.TryParse(rawDateText, out var dt))
                return dt.ToString("MM-dd-yyyy");

            return rawDateText.Trim();
        }

        private async void WorkspaceView_RunSnmpOidRequested(object? sender, SnmpRunOidRequestedEventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            if (!session.SnmpProfileId.HasValue || session.SnmpProfileId.Value == 0)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            try
            {
                session.SnmpTargetIp = targetIp;
                WorkspaceView.SetSnmpOidResult(e.Oid.Id, "Running...");

                var result = await _api.PostAsync<SnmpRunSelectedRequestDto, SnmpRunResultDto>(
                    "api/snmp-profiles/run-selected",
                    new SnmpRunSelectedRequestDto
                    {
                        ProfileId = session.SnmpProfileId.Value,
                        OidId = e.Oid.Id,
                        TargetIp = targetIp
                    });

                var display = result?.Success == true
                    ? result.DisplayValue
                    : $"ERROR: {result?.ErrorMessage}";

                session.SnmpOidResults[e.Oid.Id] = display ?? string.Empty;
                WorkspaceView.SetSnmpOidResult(e.Oid.Id, display ?? string.Empty);

                TopBarView.StatusText = result?.Success == true
                    ? $"SNMP poll returned {result.DisplayValue}."
                    : $"SNMP poll failed: {result?.ErrorMessage}";
            }
            catch (Exception ex)
            {
                var error = $"ERROR: {ex.Message}";
                session.SnmpOidResults[e.Oid.Id] = error;
                WorkspaceView.SetSnmpOidResult(e.Oid.Id, error);
                TopBarView.StatusText = $"SNMP poll failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_RunSnmpCategoryRequested(object? sender, SnmpRunCategoryRequestedEventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            if (!session.SnmpProfileId.HasValue || session.SnmpProfileId.Value == 0)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            try
            {
                session.SnmpTargetIp = targetIp;

                foreach (var oid in e.Oids.OrderBy(x => x.SortOrder).ThenBy(x => x.Label))
                {
                    WorkspaceView.SetSnmpOidResult(oid.Id, "Running...");

                    try
                    {
                        var result = await _api.PostAsync<SnmpRunSelectedRequestDto, SnmpRunResultDto>(
                            "api/snmp-profiles/run-selected",
                            new SnmpRunSelectedRequestDto
                            {
                                ProfileId = session.SnmpProfileId.Value,
                                OidId = oid.Id,
                                TargetIp = targetIp
                            });

                        var display = result?.Success == true
                            ? result.DisplayValue
                            : $"ERROR: {result?.ErrorMessage}";

                        session.SnmpOidResults[oid.Id] = display ?? string.Empty;
                        WorkspaceView.SetSnmpOidResult(oid.Id, display ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        var error = $"ERROR: {ex.Message}";
                        session.SnmpOidResults[oid.Id] = error;
                        WorkspaceView.SetSnmpOidResult(oid.Id, error);
                    }
                }

                TopBarView.StatusText = $"{e.Category} SNMP poll complete.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Category poll failed: {ex.Message}";
            }
        }

        private void WorkspaceView_OpenTopTunnelRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var ip = (session.TopTunnelIp ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ip) || ip == "—")
                return;

            var url = $"https://{ip}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        //Handler to fix the "Refresh SNMP Config" button refreshing the WHOLE dashboard
        private void RenderSnmpOnly(SiteDashboardTabSession session)
        {
            WorkspaceView.SetSnmpContext(
                session.SnmpSupported,
                session.SnmpSupportMessage,
                session.SnmpDeviceFamily,
                session.SnmpProfileName,
                session.PrimaryIp,
                session.LanIp,
                session.SecondaryIp,
                session.SnmpTargetIp);

            WorkspaceView.SetSnmpProfiles(session.SnmpProfiles, session.SnmpProfileId);
            WorkspaceView.SetSnmpOids(session.SnmpOids, session.SnmpOidResults);
        }

        //Resets the ENTIRE workspace Tab back to Main and clears the SNMP state. 
        private static void ResetSessionForNewSiteLoad(SiteDashboardTabSession session)
        {
            session.AddressText = "—";
            session.CoordinatesText = "—";

            session.PrimaryIp = "—";
            session.LanIp = "—";
            session.SecondaryIp = "—";

            session.TopInfoText = string.Empty;
            session.WriteUpText = string.Empty;
            session.EquipmentText = string.Empty;
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

        //Helpers? 
        private static readonly JsonSerializerOptions _dashboardJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static T? DeserializeDashboardData<T>(SiteDashboardResponseDto? dashboard)
            where T : class
        {
            if (dashboard?.Data is JsonElement json &&
                json.ValueKind != JsonValueKind.Null &&
                json.ValueKind != JsonValueKind.Undefined)
            {
                return JsonSerializer.Deserialize<T>(json.GetRawText(), _dashboardJsonOptions);
            }

            if (dashboard?.Data is T typed)
                return typed;

            return null;
        }

        private static string DashIfEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private static string BuildAddress(string? streetNo, string? streetName, string? city, string? stateCode, string? zipCode)
        {
            var line1 = string.Join(" ", new[] { streetNo, streetName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var cityState = string.Join(", ", new[] { city, stateCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var line2 = string.Join(" ", new[] { cityState, zipCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var combined = string.Join("  ", new[] { line1, line2 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrWhiteSpace(combined) ? "—" : combined;
        }

        private static string BuildCoordinates(decimal? latitude, decimal? longitude)
        {
            if (!latitude.HasValue && !longitude.HasValue)
                return "—";

            if (!latitude.HasValue)
                return longitude!.Value.ToString();

            if (!longitude.HasValue)
                return latitude.Value.ToString();

            return $"{latitude.Value}, {longitude.Value}";
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

        private async void WorkspaceView_TicketActionRequested(object? sender, TicketActionRequestedEventArgs e)
        {
            if (_ticketActionInProgress)
            {
                TopBarView.StatusText = "Ticket action already running...";
                return;
            }

            var session = GetSelectedSession();

            if (session is null)
                return;

            try
            {
                _ticketActionInProgress = true;

                switch (e.Action)
                {
                    case "RequestCapital":
                        await HandleRequestCapitalAsync(session, e);
                        break;

                    case "RequestMaintenance":
                        await HandleRequestMaintenanceAsync(session, e);
                        break;

                    case "RequestTicket":
                        await HandleRequestTicketAsync(session, e);
                        break;
                }
            }
            finally
            {
                _ticketActionInProgress = false;
            }
        }

        private async Task HandleRequestCapitalAsync(SiteDashboardTabSession session, TicketActionRequestedEventArgs e)
        {
            if (e.TicketId <= 0)
            {
                TopBarView.StatusText = "No ticket is currently associated with this site.";
                return;
            }

            try
            {
                TopBarView.StatusText = "Requesting Capital...";

                await _ticketsApi.RequestCapitalAsync(
                    e.TicketId,
                    e.Reason,
                    requestedBy: Environment.UserName,
                    CancellationToken.None);

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Capital request saved.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Capital failed: {ex.Message}";
            }
        }

        private async Task HandleRequestMaintenanceAsync(SiteDashboardTabSession session, TicketActionRequestedEventArgs e)
        {
            if (e.TicketId <= 0)
            {
                TopBarView.StatusText = "No ticket is currently associated with this site.";
                return;
            }

            try
            {
                TopBarView.StatusText = "Requesting Maintenance...";

                await _ticketsApi.RequestMaintenanceAsync(
                    e.TicketId,
                    e.Reason,
                    requestedBy: Environment.UserName,
                    CancellationToken.None);

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Maintenance request saved.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Maintenance failed: {ex.Message}";
            }
        }

        private async Task HandleRequestTicketAsync(SiteDashboardTabSession session, TicketActionRequestedEventArgs e)
        {
            try
            {
                TopBarView.StatusText = "Creating ticket request...";

                var newTicketId = await _ticketsApi.RequestTicketAsync(
                    session.HeaderText,
                    e.Reason,
                    requestedBy: Environment.UserName,
                    CancellationToken.None);

                session.CurrentTicketId = newTicketId;

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Ticket request created.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Ticket failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_RxIpLookupRequested(object? sender, string ip)
        {
            try
            {
                TopBarView.StatusText = $"Looking up associated site for {ip}...";

                var result = await _api.GetAsync<AssociatedSiteByIpLookupDto>(
                    $"api/site-dashboard/associated-site-by-ip?ip={Uri.EscapeDataString(ip)}",
                    CancellationToken.None);

                if (result is null || !result.Found || string.IsNullOrWhiteSpace(result.SiteId))
                {
                    WorkspaceView.ShowRxIpLookupResult(
                        null,
                        $"No associated site found for {ip}.");

                    TopBarView.StatusText = "No associated site found.";
                    return;
                }

                var message = result.MatchCount > 1
                    ? $"Found {result.MatchCount} possible matches. Showing the first match from {result.MatchSource}.{result.MatchField}."
                    : $"Found match from {result.MatchSource}.{result.MatchField}.";

                WorkspaceView.ShowRxIpLookupResult(result.SiteId, message);

                TopBarView.StatusText = $"Associated site found: {result.SiteId}.";
            }
            catch (Exception ex)
            {
                WorkspaceView.ShowRxIpLookupResult(null, $"Lookup failed: {ex.Message}");
                TopBarView.StatusText = $"RX IP lookup failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_OpenAssociatedSiteRequested(object? sender, string siteId)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return;

            var existingSession = _sessions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.HeaderText, siteId, StringComparison.OrdinalIgnoreCase));

            if (existingSession is not null)
            {
                _selectedSessionKey = existingSession.SessionKey;
                RenderSelectedSession();
                TopBarView.StatusText = $"Switched to {siteId}.";
                return;
            }

            CreateBlankTab(selectNewTab: true);
            RenderSelectedSession();

            await LoadAsync(siteId);
        }

        private static void ApplyNetworkLabelsForDefault(SiteDashboardNetworkView networkView)
        {
            networkView.PrimaryPingLabel = "Primary";
            networkView.LanPingLabel = "LAN";
            networkView.SecondaryPingLabel = "Secondary";
        }

        private void ApplyNetworkLabels(SiteDashboardTabSession session)
        {
            ApplyNetworkLabelsForDefault(NetworkView);

            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Dacs, StringComparison.OrdinalIgnoreCase))
            {
                NetworkView.PrimaryPingLabel = "Primary";
                NetworkView.LanPingLabel = "Gateway";
                NetworkView.SecondaryPingLabel = "RTU";
                return;
            }

            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Igsd, StringComparison.OrdinalIgnoreCase))
            {
                NetworkView.PrimaryPingLabel = "Primary";
                NetworkView.LanPingLabel = "LAN";
                NetworkView.SecondaryPingLabel = "Secondary";
            }
        }

        private static string GetWindowsEmployeeId()
        {
            var name = WindowsIdentity.GetCurrent()?.Name ?? string.Empty;

            if (name.Contains('\\'))
                name = name.Split('\\').Last();

            if (name.Contains('@'))
                name = name.Split('@').First();

            return name.Trim();
        }

        private async Task LoadCurrentCnpTechNameAsync()
        {
            try
            {
                var employeeId = GetWindowsEmployeeId();

                if (string.IsNullOrWhiteSpace(employeeId))
                {
                    _currentCnpTechName = string.Empty;
                    WorkspaceView.CurrentCnpTechName = string.Empty;
                    return;
                }

                var crew = await _api.GetAsync<CurrentCrewDto>(
                    $"api/technicians/current-crew/{Uri.EscapeDataString(employeeId)}");

                _currentCnpTechName = string.IsNullOrWhiteSpace(crew?.DisplayText)
                    ? employeeId
                    : crew.DisplayText.Trim();

                WorkspaceView.CurrentCnpTechName = _currentCnpTechName;
            }
            catch
            {
                _currentCnpTechName = GetWindowsEmployeeId();
                WorkspaceView.CurrentCnpTechName = _currentCnpTechName;
            }
        }
    }
}