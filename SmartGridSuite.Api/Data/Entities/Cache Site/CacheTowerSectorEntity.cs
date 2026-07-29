using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CacheTowerSectorEntity
    {
        public long CacheTowerSectorId { get; set; }

        public int TopNameId { get; set; }

        public int TopSiteId { get; set; }

        public string? Sector { get; set; }

        public decimal? TxDbm { get; set; }

        public decimal? Downtilt { get; set; }

        public int? ChannelNumber { get; set; }

        public decimal? ChannelTxFrequency { get; set; }

        public decimal? ChannelRxFrequency { get; set; }

        public string? NetworkName { get; set; }

        public string? Vip { get; set; }

        public string? IpA { get; set; }

        public string? IpB { get; set; }

        public string? Vlan { get; set; }

        public string? Bsid { get; set; }

        public string? AntennaSerialA { get; set; }

        public string? AntennaSerialB { get; set; }

        public decimal? Height { get; set; }

        public decimal? TestedHeight { get; set; }

        public int? Bearing { get; set; }

        public bool? HighMount { get; set; }

        public DateTime LastSyncedAt { get; set; }

        public long? SyncRunId { get; set; }

        public string? SourceRowHash { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
