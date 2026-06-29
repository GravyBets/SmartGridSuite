namespace SmartGridSuite.Contracts.Tickets;

public sealed class BulkAssignTicketsRequest
{
    public List<long> TicketIds { get; set; } = new();

    public string AssignedTech { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
}