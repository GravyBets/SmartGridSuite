namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class SiteHistoryPreviewDto
    {
        public long HistoryId { get; set; }
        public string SiteId { get; set; } = "";
        public DateTime? VisitDate { get; set; }
        public string? PrimaryTech { get; set; }
        public string? SecondaryTech { get; set; }
        public string? IssueText { get; set; }
        public string? Narrative { get; set; }

        
    }
}