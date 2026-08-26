#nullable enable

namespace SmartGridSuite.Contracts.SiteDashboard
{
    public sealed class UpdateSiteHistoryRequest
    {
        public string? Narrative { get; set; }

        public string? IssueText { get; set; }

        public string? PrimaryTech { get; set; }

        public string? SecondaryTech { get; set; }

        public string UpdatedBy { get; set; } = "";
    }
}