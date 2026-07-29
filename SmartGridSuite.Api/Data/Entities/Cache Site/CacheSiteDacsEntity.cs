using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteDacsEntity
    {
        public long CacheSiteDacsId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceDacsRowId { get; set; }

        public string? PrimaryCommsIp { get; set; }

        public string? TunnelIp { get; set; }

        public string? RtuIp { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
