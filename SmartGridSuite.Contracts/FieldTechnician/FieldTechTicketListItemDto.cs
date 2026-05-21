#nullable enable
namespace SmartGridSuite.Contracts.FieldTechnician;

public sealed class FieldTechTicketListItemDto
{
    public long Id { get; set; }

    public string Site { get; set; } = "";
    public string NotificationName { get; set; } = "";
    public string Notification { get; set; } = "";

    public string Status { get; set; } = "";
    public string AssignedTech { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }

    public string WorkOrder { get; set; } = "";
    public string WorkOrderClass { get; set; } = "";

    public string GroupCode { get; set; } = "";
    public int PriorityDays { get; set; }

    public string Problem { get; set; } = "";
    public string Notes { get; set; } = "";

    public string Category { get; set; } = "";
    public string ActionRequired { get; set; } = "";
}