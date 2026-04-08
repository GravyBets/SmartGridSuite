namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class DacsSiteDashboardDto : ISiteDashboardDataDto
    {
        public string SiteId { get; set; } = "";
        public string? SiteStatus { get; set; }

        public string? SiteType { get; set; }

        public string? SiteConfigName { get; set; }
        public string? PrimaryCommType { get; set; }
        public string? SecondaryCommType { get; set; }
        public string? SiteConfigDescription { get; set; }

        public string? PrimaryCommsIp { get; set; }
        public string? TunnelIp { get; set; }
        public string? RtuIp { get; set; }

        public string? TopName { get; set; }
        public string? TopDescription { get; set; }
        public string? TopSector { get; set; }

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