namespace SmartGridSuite.Api.Configuration
{
    public sealed class SiteDashboardCacheRefreshOptions
    {
        public const string SectionName =
            "SiteDashboardCacheRefresh";

        public bool Enabled { get; set; } = true;

        public DayOfWeek DayOfWeek { get; set; } =
            DayOfWeek.Sunday;

        public TimeSpan TimeOfDay { get; set; } =
            new(2, 0, 0);
    }
}