namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteAmsEntity
    {
        public long CacheSiteAmsId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceAmsRowId { get; set; }

        public string? PrimaryCommsIdentifier { get; set; }

        public string? PrimaryCommsIp { get; set; }

        public string? SecondaryLanIp { get; set; }

        public string? AntennaSerialNumber { get; set; }

        public string? EnclosureSerialNumber { get; set; }

        public string? EnclosureModel { get; set; }

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