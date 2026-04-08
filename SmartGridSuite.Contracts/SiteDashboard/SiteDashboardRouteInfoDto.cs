namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class SiteDashboardRouteInfoDto
    {
        public string SiteId { get; set; } = "";

        public int? SiteTypeId { get; set; }
        public string? SiteType { get; set; }

        public int? ConfigId { get; set; }
        public string? SiteConfigName { get; set; }

        public string? PrimaryCommType { get; set; }
        public string? SecondaryCommType { get; set; }
        public string? SiteConfigDescription { get; set; }
    }
}