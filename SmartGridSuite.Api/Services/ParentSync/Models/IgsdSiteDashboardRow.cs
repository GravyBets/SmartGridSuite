using System.Collections.Generic;

namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class IgsdSiteDashboardRow
    {
        public string SiteId { get; init; } = "";

        public string? SiteStatus { get; init; }
        public string? SiteType { get; init; }

        public string? SiteConfigName { get; init; }
        public string? PrimaryCommType { get; init; }
        public string? SecondaryCommType { get; init; }
        public string? SiteConfigDescription { get; init; }

        // Primary path
        public string? PrimaryCommsIdentifier { get; init; }
        public string? PrimaryCommsIp { get; init; }
        public string? PrimaryLanIp { get; init; }
        public string? PrimaryWanIp { get; init; }
        public string? PrimaryTunnelIp { get; init; }
        public string? PrimaryRtuIp { get; init; }

        // Secondary path
        public string? SecondaryCommsIdentifier { get; init; }
        public string? SecondaryWanIp { get; init; }
        public string? SecondaryLanIp { get; init; }
        public string? SecondaryTunnelIp { get; init; }
        public string? SecondaryRtuIp { get; init; }

        // Shared hardware / security
        public string? AntennaSerialNumber { get; init; }
        public string? EnclosureSerialNumber { get; init; }
        public string? EnclosureModel { get; init; }
        public string? CyberlockSerialNumber { get; init; }
        public string? TunnelPsk { get; init; }

        // TOP
        public string? TopName { get; init; }
        public string? TopDescription { get; init; }
        public string? TopSector { get; init; }
        public string? TopVip { get; init; }
        public string? TopIpA { get; init; }
        public string? TopIpB { get; init; }

        // Address
        public string? StreetNo { get; init; }
        public string? StreetName { get; init; }
        public string? City { get; init; }
        public string? County { get; init; }
        public string? StateCode { get; init; }
        public string? ZipCode { get; init; }

        // GPS
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }

        // History
        public List<SiteHistoryPreviewRow> History { get; init; } = new();
    }
}