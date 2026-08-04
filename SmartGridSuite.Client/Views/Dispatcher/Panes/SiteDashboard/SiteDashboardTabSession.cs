using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using System.Collections.Generic;
using System.Threading;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public sealed class SiteDashboardTabSession
    {
        public List<EquipmentReplacementSessionEntry> EquipmentReplacementEntries { get; set; } = new();
        public NetworkPingSessionState? NetworkPingState { get; set; }
        public string DashboardKind { get; set; } = string.Empty;
        public string SessionKey { get; init; } = string.Empty;
        public string HeaderText { get; set; } = "Blank";
        public string SearchText { get; set; } = string.Empty;
        public string AddressText { get; set; } = "—";
        public string CoordinatesText { get; set; } = "—";
        public string TopTunnelIp { get; set; } = "—";
        public string PrimaryIp { get; set; } = "—";
        public string LanIp { get; set; } = "—";
        public string SecondaryIp { get; set; } = "—";
        public string TopInfoText { get; set; } = string.Empty;
        public string WriteUpText { get; set; } = string.Empty;
        public string EquipmentText { get; set; } = string.Empty;
        public string SelectedWorkspaceTabKey { get; set; } = "TopWriteUp";
        public string SiteStatusText { get; set; } = string.Empty;
        public string TopAccessTitleText { get; set; } = "TOP Access";

        // Submit options
        public SiteDashboardSubmitOptionsSessionState SubmitOptions { get; set; } = new();

        // Range Extender
        public string RangeExtenderLinkUrl { get; set; } = "";

        //IGSD   
        public string IgsdPrimaryRtuIp { get; set; } = "—";

        //For Dual Cell Sites
        public string IgsdPrimaryCommsEthernetIp { get; set; } = "—";
        public string IgsdSecondaryCommsEthernetIp { get; set; } = "—";
        public string IgsdSecondaryRtuIp { get; set; } = "—";
        public string IgsdPrimaryTunnelIp { get; set; } = "—";
        public bool ShowIgsdPortalTab { get; set; }
        public string IgsdPortalUrl { get; set; } = "";

        //Tickets
        public string TicketInfoText { get; set; } = string.Empty;
        public List<SiteDashboardHistoryRowViewModel> HistoryRows { get; set; } = new();
        public long CurrentTicketId { get; set; }

        //SNMP
        public bool SnmpSupported { get; set; }
        public string SnmpSupportMessage { get; set; } = string.Empty;
        public string SnmpDeviceFamily { get; set; } = string.Empty;
        public string SnmpProfileName { get; set; } = string.Empty;
        public string SnmpPrimaryCommType { get; set; } = string.Empty;
        public string SnmpTargetIp { get; set; } = string.Empty;
        public List<SnmpOidConfigDto> SnmpOids { get; set; } = new();
        public List<SnmpProfileListItemDto> SnmpProfiles { get; set; } = new();
        public ulong? SnmpProfileId { get; set; }

        public SnmpProfileDetailDto? SnmpProfile { get; set; }
        public Dictionary<ulong, string> SnmpOidResults { get; set; } = new();

        //Towers
        public int? TowerTopNameId { get; set; }
        public string TowerSummaryText { get; set; } = string.Empty;
        public List<TowerSectorDto> TowerSectors { get; set; } = new();
        public TowerPingSessionState? TowerPingState { get; set; }

        //Popped Out
        public bool IsPoppedOut { get; set; }

    }

    public sealed class SiteDashboardSubmitOptionsSessionState
    {
        public bool IncludePingStats { get; set; } = true;

        public bool IncludeSnmpStats { get; set; } = false;
        public bool IncludeSnmpAdmin { get; set; } = true;
        public bool IncludeSnmpConfig { get; set; } = true;
        public bool IncludeSnmpStatsCategory { get; set; } = true;
    }
    public sealed class EquipmentReplacementSessionEntry
    {
        public string SlotLabel { get; set; } = "";
        public bool UsesCommunicationDeviceTypePicker { get; set; }
        public string Item { get; set; } = "";
        public string OldSerial { get; set; } = "";
        public string NewSerial { get; set; } = "";
        public string ReplacementKey { get; set; } = "";
    }

    public sealed class NetworkPingSessionState
    {
        public string PingCount { get; set; } = "";

        public NetworkPingTargetState Primary { get; set; } = new();
        public NetworkPingTargetState Lan { get; set; } = new();
        public NetworkPingTargetState Secondary { get; set; } = new();

        public string IgsdPrimaryRtuIp { get; set; } = "";
        public string IgsdPrimaryCommsEthernetIp { get; set; } = "";
        public string IgsdSecondaryCommsEthernetIp { get; set; } = "";
        public string IgsdSecondaryRtuIp { get; set; } = "";
    }

    public sealed class NetworkPingTargetState
    {
        public string Ip { get; set; } = "";

        public string Results { get; set; } = "";

        public string Summary { get; set; } = "Ready.";

        public bool? TestSuccessful { get; set; }

        /*
         * Runtime-only state. Each Site Dashboard tab owns its own
         * cancellation token, allowing pings to continue while another
         * dashboard tab is selected.
         */
        public CancellationTokenSource? PingCts { get; set; }
    }

    public sealed class TowerPingSessionState
    {
        public List<TowerSectorPingSessionState> Sectors { get; set; } = new();
    }

    public sealed class TowerSectorPingSessionState
    {
        public string Sector { get; set; } = "";
        public string PingCount { get; set; } = "";
        public List<TowerEndpointPingSessionState> Endpoints { get; set; } = new();
    }

    public sealed class TowerEndpointPingSessionState
    {
        public string Label { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string Results { get; set; } = "";
        public string Summary { get; set; } = "Ready.";
        public bool? TestSuccessful { get; set; }
    }



}