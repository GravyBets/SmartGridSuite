namespace SmartGridSuite.Contracts.Tickets;

public sealed class SubmittedWriteUpMutationResponse
{
    public long SubmissionId { get; set; }

    public long? SiteHistoryId { get; set; }

    public long TicketId { get; set; }

    public DateTime ChangedAt { get; set; }
}