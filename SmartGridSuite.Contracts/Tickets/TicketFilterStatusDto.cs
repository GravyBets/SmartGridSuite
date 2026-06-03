namespace SmartGridSuite.Contracts.Tickets;

public sealed class TicketFilterStatusDto
{
    public string Name { get; set; } = "";

    public int SortOrder { get; set; }

    public bool IsClosed { get; set; }
}