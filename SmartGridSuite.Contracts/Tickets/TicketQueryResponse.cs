namespace SmartGridSuite.Contracts.Tickets;

public sealed class TicketQueryResponse
{
    public List<TicketListItemDto> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int ReturnedCount => Items.Count;
}