using System.Collections.Generic;

namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class TowerDashboardDto
    {
        public int TopNameId { get; set; }

        public string? TopName { get; set; }
        public string? TopType { get; set; }
        public string? TopDescription { get; set; }

        public string? IpAssignment { get; set; }
        public int? GpsId { get; set; }
        public int? CnpAreaId { get; set; }
        public bool? CustomerOwned { get; set; }
        public string? Note { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        public string? StreetNo { get; set; }
        public string? StreetName { get; set; }
        public string? City { get; set; }
        public string? County { get; set; }
        public string? StateCode { get; set; }
        public string? ZipCode { get; set; }
        public string? FullAddress { get; set; }

        public List<TowerSectorDto> Sectors { get; set; } = new();

        public string? HistorySiteId { get; set; }

        public List<SiteHistoryPreviewDto> History { get; set; } = new();
    }
}