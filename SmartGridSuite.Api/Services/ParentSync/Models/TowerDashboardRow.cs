using System.Collections.Generic;

namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class TowerDashboardRow
    {
        public int TopNameId { get; init; }

        public string? TopName { get; init; }
        public string? TopType { get; init; }
        public string? TopDescription { get; init; }

        public string? IpAssignment { get; init; }
        public int? GpsId { get; init; }
        public int? CnpAreaId { get; init; }
        public bool? CustomerOwned { get; init; }
        public string? Note { get; init; }

        public List<TowerSectorRow> Sectors { get; init; } = new();
    }
}