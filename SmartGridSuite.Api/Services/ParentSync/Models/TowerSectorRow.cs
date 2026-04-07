namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class TowerSectorRow
    {
        public int TopSiteId { get; init; }
        public string? Sector { get; init; }

        public decimal? TxWatts { get; init; }
        public decimal? TxDbm { get; init; }
        public decimal? ErpWatts { get; init; }
        public decimal? Downtilt { get; init; }

        public int? Channel { get; init; }
        public string? Network { get; init; }

        public string? Vip { get; init; }
        public string? IPa { get; init; }
        public string? IPb { get; init; }
        public string? Vlan { get; init; }

        public string? Bsid { get; init; }
        public string? ApModel { get; init; }
        public string? ApFrequency { get; init; }

        public string? AntennaSerialA { get; init; }
        public string? AntennaSerialB { get; init; }

        public decimal? Height { get; init; }
        public decimal? TestedHeight { get; init; }
        public int? Bearing { get; init; }
        public bool? HighMount { get; init; }
    }
}