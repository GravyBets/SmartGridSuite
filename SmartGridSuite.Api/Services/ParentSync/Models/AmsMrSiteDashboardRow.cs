namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class AmsMrSiteDashboardRow
    {
        public string SiteId { get; init; } = "";
        public string? SiteStatus { get; init; }

        public string? SiteType { get; init; }
        public string? SiteConfigName { get; init; }
        public string? PrimaryCommType { get; init; }
        public string? SecondaryCommType { get; init; }
        public string? SiteConfigDescription { get; init; }

        public string? PrimaryCommsIdentifier { get; init; }
        public string? PrimaryCommsIp { get; init; }

        public string? SecondaryLanIp { get; init; }
        public string? SecondaryWanIp { get; init; }

        public string? SecondaryCommsIdentifier { get; init; }
        public string? SecondaryCommsUsername { get; init; }
        public string? SecondaryCommsSsid { get; init; }
        public string? SecondaryCommsPassword { get; init; }
        public string? SecondarySimNumber { get; init; }

        public string? AntennaSerialNumber { get; init; }
        public string? EnclosureSerialNumber { get; init; }
        public string? EnclosureModel { get; init; }

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