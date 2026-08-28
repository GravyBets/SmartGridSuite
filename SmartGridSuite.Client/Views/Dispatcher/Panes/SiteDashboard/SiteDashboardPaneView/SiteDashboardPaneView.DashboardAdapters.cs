using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.SiteDashboard;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        //Choose which Dashboard to Render
        private void ApplyDashboardToSession(SiteDashboardTabSession session, SiteDashboardResponseDto? dashboard,
            string requestedSiteId)
        {
            session.DashboardKind =
                dashboard?.DashboardKind
                ?? string.Empty;

            switch (session.DashboardKind)
            {
                case SiteDashboardKinds.AmsMr:
                    ApplyAmsMrDashboard(
                        session,
                        dashboard,
                        requestedSiteId);
                    break;

                case SiteDashboardKinds.Igsd:
                    ApplyIgsdDashboard(
                        session,
                        dashboard,
                        requestedSiteId);
                    break;

                case SiteDashboardKinds.Dacs:
                    ApplyDacsDashboard(
                        session,
                        dashboard,
                        requestedSiteId);
                    break;

                case SiteDashboardKinds.Rx:
                    ApplyRxDashboard(
                        session,
                        dashboard,
                        requestedSiteId);
                    break;

                case SiteDashboardKinds.Tower:
                    ApplyTowerDashboard(
                        session,
                        dashboard,
                        requestedSiteId);
                    break;

                default:
                    ApplyFallbackDashboard(
                        session,
                        dashboard,
                        requestedSiteId);
                    break;
            }

            session.SnmpPrimaryCommType =
                dashboard?.Route?.PrimaryCommType?.Trim()
                ?? string.Empty;

            if (dashboard?.IsCached == true)
            {
                var existingStatus =
                    (session.SiteStatusText ?? string.Empty)
                    .Trim();

                session.SiteStatusText =
                    string.IsNullOrWhiteSpace(existingStatus) ||
                    existingStatus == "—"
                        ? "Cached"
                        : $"{existingStatus} • Cached";
            }
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
        private async Task ApplyPingScreenPortalUrlAsync(
            SiteDashboardTabSession session,
            SiteDashboardResponseDto? dashboard,
            CancellationToken ct)
        {
            var dashboardKind =
                !string.IsNullOrWhiteSpace(dashboard?.DashboardKind)
                    ? dashboard.DashboardKind
                    : session.DashboardKind;

            var showPingScreen =
                string.Equals(
                    dashboardKind,
                    SiteDashboardKinds.Igsd,
                    StringComparison.OrdinalIgnoreCase) ||

                string.Equals(
                    dashboardKind,
                    SiteDashboardKinds.Dacs,
                    StringComparison.OrdinalIgnoreCase) ||

                (
                    _accessMode == SiteDashboardAccessMode.Lineman &&
                    string.Equals(
                        dashboardKind,
                        SiteDashboardKinds.AmsMr,
                        StringComparison.OrdinalIgnoreCase)
                );

            if (!showPingScreen)
            {
                session.ShowIgsdPortalTab = false;
                session.IgsdPortalUrl = string.Empty;
                return;
            }

            var dto =
                await _api.GetIgsdPortalUrlAsync(ct);

            var url =
                (dto?.Url ?? string.Empty).Trim();

            session.ShowIgsdPortalTab =
                !string.IsNullOrWhiteSpace(url);

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
                $"Range Extender SN: {DashIfEmpty(dto?.MeterNumber)}{Environment.NewLine}" +
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
    }
}