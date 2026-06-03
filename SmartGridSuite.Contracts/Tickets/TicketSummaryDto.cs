namespace SmartGridSuite.Contracts.Tickets;

public sealed class TicketSummaryDto
{
    public int TotalCount { get; set; }

    public List<TicketSummaryStatusDto> Statuses { get; set; } = new();
}

public sealed class TicketSummaryStatusDto
{
    public string Status { get; set; } = "";

    public int Count { get; set; }

    public int SortOrder { get; set; }
}