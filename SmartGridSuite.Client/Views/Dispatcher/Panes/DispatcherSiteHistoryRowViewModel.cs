#nullable enable

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public sealed class DispatcherSiteHistoryRowViewModel
    {
        public long HistoryId { get; init; }

        public long? SubmissionId { get; init; }

        public string SiteId { get; init; } = "";

        public string SourceType { get; init; } = "";

        public DateTime? SubmittedAt { get; init; }

        public DateTime? VisitDate { get; init; }

        public string DateTimeText { get; init; } = "—";

        public string Tech1Text { get; init; } = "—";

        public string Tech2Text { get; init; } = "—";

        public string IssueText { get; init; } = "";

        public string NarrativeText { get; init; } = "";

        public DateTime? EditedAt { get; init; }

        public string EditedBy { get; init; } = "";

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