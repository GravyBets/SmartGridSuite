namespace SmartGridSuite.Contracts.Tickets;

public sealed class BulkSetProblemRequest
{
    public List<long> TicketIds { get; set; } = new();

    public string Problem { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
}