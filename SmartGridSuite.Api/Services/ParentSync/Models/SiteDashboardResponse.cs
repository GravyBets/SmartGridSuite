namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class SiteDashboardResponse
    {
        public string SiteId { get; init; } = "";
        public string DashboardKind { get; init; } = "";
        public SiteDashboardRouteInfo Route { get; init; } = new();
        public object Data { get; init; } = new();
    }
}