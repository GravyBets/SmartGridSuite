namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class SiteHistoryPreviewRow
    {
        public long HistoryId { get; init; }
        public string SiteId { get; init; } = "";
        public DateTime? VisitDate { get; init; }
        public string? PrimaryTech { get; init; }
        public string? SecondaryTech { get; init; }
        public string? Narrative { get; init; }
    }
}