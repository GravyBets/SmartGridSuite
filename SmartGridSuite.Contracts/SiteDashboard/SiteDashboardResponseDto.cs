namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class SiteDashboardResponseDto
    {
        public string SiteId { get; set; } = "";
        public string DashboardKind { get; set; } = "";
        public SiteDashboardRouteInfoDto Route { get; set; } = new();
        public object? Data { get; set; }
    }
}