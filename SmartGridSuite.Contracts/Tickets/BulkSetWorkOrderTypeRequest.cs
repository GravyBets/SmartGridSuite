namespace SmartGridSuite.Contracts.Tickets;

public sealed class BulkSetWorkOrderTypeRequest
{
    public List<long> TicketIds { get; set; } = new();

    public string WorkOrderType { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
}