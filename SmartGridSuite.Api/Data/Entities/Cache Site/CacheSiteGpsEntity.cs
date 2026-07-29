using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteGpsEntity
    {
        public long CacheSiteGpsId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceGpsRowId { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public decimal? Elevation { get; set; }

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