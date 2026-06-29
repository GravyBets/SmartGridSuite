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

    // Used by the field UI to grey out completed work without hiding it.
    public bool IsFieldComplete { get; set; }

    // Populated only for published Daily Assignments route items.
    public int? RouteOrder { get; set; }

    // Lets the field UI show a visible cue when expandable row details contain data.
    public bool HasDispatchNotes { get; set; }

    public int SiteNoteCount { get; set; }

    public bool HasExpandableDetails =>
        HasDispatchNotes || SiteNoteCount > 0;
}