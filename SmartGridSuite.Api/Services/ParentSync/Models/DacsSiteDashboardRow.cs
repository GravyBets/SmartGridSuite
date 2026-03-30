namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class DacsSiteDashboardRow
    {
        public string SiteId { get; init; } = "";
        public string? SiteStatus { get; init; }

        public string? SiteType { get; init; }

        public string? SiteConfigName { get; init; }
        public string? PrimaryCommType { get; init; }
        public string? SecondaryCommType { get; init; }
        public string? SiteConfigDescription { get; init; }

        public string? PrimaryCommsIp { get; init; }
        public string? TunnelIp { get; init; }
        public string? RtuIp { get; init; }

        public string? TopName { get; init; }
        public string? TopDescription { get; init; }
        public string? TopSector { get; init; }

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