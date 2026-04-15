using SmartGridSuite.Contracts.Snmp;
using System.Collections.Generic;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public sealed class SiteDashboardTabSession
    {
        public string SessionKey { get; init; } = string.Empty;
        public string HeaderText { get; set; } = "Blank";
        public string SearchText { get; set; } = string.Empty;

        public string AddressText { get; set; } = "—";
        public string CoordinatesText { get; set; } = "—";

        public string PrimaryIp { get; set; } = "—";
        public string LanIp { get; set; } = "—";
        public string SecondaryIp { get; set; } = "—";

        public string TopInfoText { get; set; } = string.Empty;
        public string WriteUpText { get; set; } = string.Empty;
        public string EquipmentText { get; set; } = string.Empty;
        public string SelectedWorkspaceTabKey { get; set; } = "TopWriteUp";

        public string SiteStatusText { get; set; } = string.Empty;
        public string TopAccessTitleText { get; set; } = "TOP Access";

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

        public ulong? SnmpProfileId { get; set; }
    }
}