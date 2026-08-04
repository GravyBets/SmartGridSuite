namespace SmartGridSuite.Contracts.Tickets;

public sealed class TicketQueryRequest
{
    public string? Search { get; set; }

    public List<string> Statuses { get; set; } = new();

    public bool ApplyStatusFilter { get; set; }

    public string? AssignedTech { get; set; }

    public string DateField { get; set; } = "LastActivity";
    // LastActivity or Created

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public string? QuickFilter { get; set; }
    // None, MissingProblems, MissingWorkOrderType, Unassigned, Assigned

    public int Skip { get; set; } = 0;

    public int Take { get; set; } = 500;
}