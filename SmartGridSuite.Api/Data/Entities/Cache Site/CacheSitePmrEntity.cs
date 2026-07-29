using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSitePmrEntity
    {
        public long CacheSitePmrId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourcePmrRowId { get; set; }

        public string? SecondaryCommsIdentifier { get; set; }

        public string? SecondaryCommsUsername { get; set; }

        public string? SecondaryCommsSsid { get; set; }

        public string? SecondaryCommsPassword { get; set; }

        public DateTime? SourceAddedAt { get; set; }

        public string? SourceAddedBy { get; set; }

        public DateTime? SourceModifiedAt { get; set; }

        public string? SourceModifiedBy { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}