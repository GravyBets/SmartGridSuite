namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class TowerSearchRow
    {
        public int TopNameId { get; init; }

        public string? TopName { get; init; }
        public string? TopType { get; init; }
        public string? TopDescription { get; init; }

        public int SectorCount { get; init; }
    }
}