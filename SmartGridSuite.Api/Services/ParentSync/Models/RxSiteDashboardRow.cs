using System.Collections.Generic;

namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class RxSiteDashboardRow
    {
        public string SiteId { get; init; } = "";

        public string? SiteStatus { get; init; }
        public string? SiteType { get; init; }

        public string? SiteConfigName { get; init; }
        
        public string? SiteConfigDescription { get; init; }

        public string? MeterNumber { get; init; }
        public string? MacAddress { get; init; }
        public string? PolePoint { get; init; }
        public string? TransformerGln { get; init; }

        public string? StreetNo { get; init; }
        public string? StreetName { get; init; }
        public string? City { get; init; }
        public string? County { get; init; }
        public string? StateCode { get; init; }
        public string? ZipCode { get; init; }

        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }

        public List<SiteHistoryPreviewRow> History { get; init; } = new();
    }
}