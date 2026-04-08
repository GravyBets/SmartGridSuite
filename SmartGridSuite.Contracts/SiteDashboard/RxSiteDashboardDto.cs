namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class RxSiteDashboardDto : ISiteDashboardDataDto
    {
        public string SiteId { get; set; } = "";

        public string? SiteStatus { get; set; }
        public string? SiteType { get; set; }

        public string? SiteConfigName { get; set; }
        public string? SiteConfigDescription { get; set; }

        public string? MeterNumber { get; set; }
        public string? MacAddress { get; set; }
        public string? PolePoint { get; set; }
        public string? TransformerGln { get; set; }

        public string? StreetNo { get; set; }
        public string? StreetName { get; set; }
        public string? City { get; set; }
        public string? County { get; set; }
        public string? StateCode { get; set; }
        public string? ZipCode { get; set; }

        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        public List<SiteHistoryPreviewDto> History { get; set; } = new();
    }
}