namespace SmartGridSuite.Api.Services.ParentSync.Models
{
    public sealed class SiteHistoryPreviewRow
    {
        public long HistoryId { get; init; }
        public string SiteId { get; init; } = "";
        public DateTime? VisitDate { get; init; }
        public DateTime? SubmittedAt { get; set; }
        public string? PrimaryTech { get; init; }
        public string? SecondaryTech { get; init; }
        public string? IssueText { get; set; }
        public string? Narrative { get; init; }

        public long? SubmissionId { get; set; }

        public string? SourceType { get; set; }

        public DateTime? EditedAt { get; set; }

        public string? EditedBy { get; set; }

        public bool IsDeleted { get; set; }
    }
}