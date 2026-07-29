namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheSiteLteEntity
    {
        public long CacheSiteLteId { get; set; }

        public string SiteId { get; set; } = "";

        public long? SourceLteRowId { get; set; }

        public string? SimNumber { get; set; }

        public string? SecondaryWanIp { get; set; }

        public string? SecondaryWanIp2 { get; set; }

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