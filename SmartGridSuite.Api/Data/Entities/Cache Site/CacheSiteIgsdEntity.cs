using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteIgsdEntity
    {
        public long CacheSiteIgsdId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceIgsdRowId { get; set; }

        public string? PrimaryCommsIdentifier { get; set; }

        public string? PrimaryCommsIp { get; set; }

        public string? PrimaryLanIp { get; set; }

        public string? PrimaryWanIp { get; set; }

        public string? PrimaryTunnelIp { get; set; }

        public string? PrimaryRtuIp { get; set; }

        public string? SecondaryCommsIdentifier { get; set; }

        public string? SecondaryWanIp { get; set; }

        public string? SecondaryLanIp { get; set; }

        public string? SecondaryTunnelIp { get; set; }

        public string? SecondaryRtuIp { get; set; }

        public string? AntennaSerialNumber { get; set; }

        public string? EnclosureSerialNumber { get; set; }

        public string? EnclosureModel { get; set; }

        public string? CyberlockSerialNumber { get; set; }

        public string? TunnelPsk { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
