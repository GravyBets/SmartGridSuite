using System.Collections.Generic;

namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class ParentCacheSnapshot
    {
        public List<AmsSiteDashboardRow> AmsSites { get; init; } = new();

        public List<DacsSiteDashboardRow> DacsSites { get; init; } = new();

        public List<IgsdSiteDashboardRow> IgsdSites { get; init; } = new();

        public List<RxSiteDashboardRow> RxSites { get; init; } = new();

        public List<TowerDashboardRow> Towers { get; init; } = new();
    }
}