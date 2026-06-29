namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public sealed class SiteDashboardHistoryRowViewModel
    {
        public SiteDashboardHistoryRowViewModel(
            string dateText,
            string tech1Text,
            string tech2Text,
            string issueText,
            string narrativeText)
        {
            DateText = dateText;
            Tech1Text = tech1Text;
            Tech2Text = tech2Text;
            IssueText = issueText;
            NarrativeText = narrativeText;
        }

        public string DateText { get; }

        public string Tech1Text { get; }

        public string Tech2Text { get; }

        public string IssueText { get; }

        public string NarrativeText { get; }

        public long? SiteHistoryId { get; init; }

        public long? SubmissionId { get; init; }

        public string SourceType { get; init; } = "";

        public string EditedBy { get; init; } = "";

        public DateTime? EditedAt { get; init; }

        public bool CanEditWriteUp =>
            SubmissionId.HasValue &&
            SubmissionId.Value > 0 &&
            SourceType.Equals("SmartGridSuite", StringComparison.OrdinalIgnoreCase);

        public string EditedText =>
            EditedAt.HasValue && !string.IsNullOrWhiteSpace(EditedBy)
                ? $"Edited by {EditedBy} on {EditedAt.Value:MM/dd/yyyy HH:mm}"
                : "";
    }
}