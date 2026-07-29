using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheTowerEntity
    {
        public long CacheTowerId { get; set; }

        public int TopNameId { get; set; }

        public string? TopName { get; set; }

        public string? TopType { get; set; }

        public string? TopDescription { get; set; }

        public string? IpAssignment { get; set; }

        public int? SourceGpsId { get; set; }

        public int? SourceCnpAreaId { get; set; }

        public bool? CustomerOwned { get; set; }

        public string? Note { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public string? StreetNumber { get; set; }

        public string? StreetName { get; set; }

        public string? StreetAddress { get; set; }

        public string? City { get; set; }

        public string? County { get; set; }

        public string? StateCode { get; set; }

        public string? ZipCode { get; set; }

        public string? FullAddress { get; set; }

        public string? HistorySiteId { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
