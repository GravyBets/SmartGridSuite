namespace SmartGridSuite.Contracts.Tickets;

public sealed class TicketWriteUpHistoryResponse
{
    public bool HasSubmissionHistory { get; set; }

    public string Text { get; set; } = "";
}