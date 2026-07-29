namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteEntity
    {
        public long CacheSiteId { get; set; }

        public string SiteId { get; set; } = "";

        public string? SiteTypeCode { get; set; }

        public string? SiteStatus { get; set; }

        public string? SiteConfigName { get; set; }

        public string? PrimaryCommType { get; set; }

        public string? SecondaryCommType { get; set; }

        public string? SiteConfigDescription { get; set; }

        public long? SourceSiteRowId { get; set; }

        public int? SourceSiteTypeId { get; set; }

        public int? SourceConfigId { get; set; }

        public int? SourceSiteStatusId { get; set; }

        public int? SourceSiteStatus2Id { get; set; }

        public long? SourceAddressRowId { get; set; }

        public long? SourceGpsRowId { get; set; }

        public long? SourceTowerApId { get; set; }

        public long? SourceTowerApAlt1Id { get; set; }

        public long? SourceTowerApAlt2Id { get; set; }

        public bool? MonitorEnabled { get; set; }

        public string? PreferredInterface { get; set; }

        public string? ServiceCenter { get; set; }

        public string? Psec { get; set; }

        public string? FunctionalLocationArea { get; set; }

        public string? SiteNotes { get; set; }

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