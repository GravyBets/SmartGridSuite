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

        // GPS / location
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }

        // Address
        public string? StreetNo { get; init; }
        public string? StreetName { get; init; }
        public string? City { get; init; }
        public string? County { get; init; }
        public string? StateCode { get; init; }
        public string? ZipCode { get; init; }
        public string? FullAddress { get; init; }

        public List<TowerSectorRow> Sectors { get; init; } = new();

        public string? HistorySiteId { get; init; }
        public List<SiteHistoryPreviewRow> History { get; init; } = new();

    }
}