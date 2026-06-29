namespace SmartGridSuite.Contracts.Tickets;

public sealed class UpdateSubmittedWriteUpRequest
{
    public string Narrative { get; set; } = "";

    public string? IssueText { get; set; }

    public string UpdatedBy { get; set; } = "";
}