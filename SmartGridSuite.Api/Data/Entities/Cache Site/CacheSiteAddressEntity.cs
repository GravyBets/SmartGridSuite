using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteAddressEntity
    {
        public long CacheSiteAddressId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceAddressRowId { get; set; }

        public string? StreetNumber { get; set; }

        public string? StreetName { get; set; }

        public string? StreetSuffix { get; set; }

        public string? StreetAddress { get; set; }

        public string? City { get; set; }

        public string? County { get; set; }

        public string? StateCode { get; set; }

        public string? ZipCode { get; set; }

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