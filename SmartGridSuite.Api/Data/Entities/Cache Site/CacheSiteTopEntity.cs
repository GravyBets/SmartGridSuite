using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteTopEntity
    {
        public long CacheSiteTopId { get; set; }

        public string SiteId { get; set; } = "";

        public int? SourceTopNameId { get; set; }

        public string? TopName { get; set; }

        public string? TopDescription { get; set; }

        public string? TopSector { get; set; }

        public string? TopVip { get; set; }

        public string? TopIpA { get; set; }

        public string? TopIpB { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
