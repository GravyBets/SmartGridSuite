namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class TowerSearchResultDto
    {
        public int TopNameId { get; set; }

        public string? TopName { get; set; }
        public string? TopType { get; set; }
        public string? TopDescription { get; set; }

        public int SectorCount { get; set; }
    }
}