namespace SmartGridSuite.Contracts.Tickets
{
    public sealed class DeleteTicketRequest
    {
        public bool ConfirmPermanentDelete { get; set; }

        public string DeletedBy { get; set; } = "";
    }

    public sealed class DeleteTicketResponse
    {
        public long TicketId { get; set; }

        public string Site { get; set; } = "";

        public string Notification { get; set; } = "";

        public int DraftAssignmentCount { get; set; }

        public int PublishedAssignmentCount { get; set; }

        public int WriteUpSubmissionCount { get; set; }

        public int WriteUpParticipantCount { get; set; }

        public int PreservedSiteHistoryCount { get; set; }
    }
}