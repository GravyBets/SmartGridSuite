using System.Collections.Generic;

namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class AssociatedSiteByIpLookupDto
    {
        public string IpAddress { get; set; } = "";
        public bool Found { get; set; }

        // Backward-compatible first/best match fields
        public string SiteId { get; set; } = "";
        public string MatchSource { get; set; } = "";
        public string MatchField { get; set; } = "";
        public int MatchCount { get; set; }

        // New full match list
        public List<AssociatedSiteByIpMatchDto> Matches { get; set; } = new();
    }

    public sealed class AssociatedSiteByIpMatchDto
    {
        public string SiteId { get; set; } = "";
        public string DashboardKind { get; set; } = "";
        public string MatchSource { get; set; } = "";
        public string MatchField { get; set; } = "";
    }
}