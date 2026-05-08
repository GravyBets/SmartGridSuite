namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class TowerSectorDto
    {
        public int TopSiteId { get; set; }
        public string? Sector { get; set; }

        public decimal? TxDbm { get; set; }
        public decimal? Downtilt { get; set; }

        public int? Channel { get; set; }
        public decimal? ChannelTxFrequency { get; set; }
        public decimal? ChannelRxFrequency { get; set; }

        public string? Network { get; set; }

        public string? Vip { get; set; }
        public string? IPa { get; set; }
        public string? IPb { get; set; }
        public string? Vlan { get; set; }

        public string? Bsid { get; set; }

        public string? AntennaSerialA { get; set; }
        public string? AntennaSerialB { get; set; }

        public decimal? Height { get; set; }
        public decimal? TestedHeight { get; set; }
        public int? Bearing { get; set; }
        public bool? HighMount { get; set; }
    }
}