namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class AssociatedSiteByIpLookupDto
    {
        public string IpAddress { get; set; } = "";
        public bool Found { get; set; }
        public string SiteId { get; set; } = "";
        public string MatchSource { get; set; } = "";
        public string MatchField { get; set; } = "";
        public int MatchCount { get; set; }
    }
}