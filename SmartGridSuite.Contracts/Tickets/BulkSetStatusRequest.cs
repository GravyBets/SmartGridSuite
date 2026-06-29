namespace SmartGridSuite.Contracts.Tickets;

public sealed class BulkSetStatusRequest
{
    public List<long> TicketIds { get; set; } = new();

    public string Status { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
}