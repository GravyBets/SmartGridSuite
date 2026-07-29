using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteRxEntity
    {
        public long CacheSiteRxId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceRxRowId { get; set; }

        public string? MeterNumber { get; set; }

        public string? MacAddress { get; set; }

        public string? PolePoint { get; set; }

        public string? TransformerGln { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
