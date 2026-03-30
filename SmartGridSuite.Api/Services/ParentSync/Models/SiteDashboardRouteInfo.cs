namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class SiteDashboardRouteInfo
    {
        public string SiteId { get; init; } = "";

        public int? SiteTypeId { get; init; }
        public string? SiteType { get; init; }

        public int? ConfigId { get; init; }
        public string? SiteConfigName { get; init; }

        public string? PrimaryCommType { get; init; }
        public string? SecondaryCommType { get; init; }
        public string? SiteConfigDescription { get; init; }
    }
}