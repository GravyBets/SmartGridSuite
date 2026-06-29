#nullable enable
namespace SmartGridSuite.Contracts.FieldTechnician;

public sealed class FieldTechHistoryItemDto
{
    public long SubmissionId { get; set; }

    public long TicketId { get; set; }

    public long? SiteHistoryId { get; set; }

    public DateTime CompletedAt { get; set; }

    public string Site { get; set; } = "";

    public string NotificationName { get; set; } = "";

    public string Notification { get; set; } = "";

    public string CurrentWorkOrder { get; set; } = "";

    public string CurrentWorkOrderType { get; set; } = "";

    public string CurrentStatus { get; set; } = "";

    public string Problem { get; set; } = "";

    public string SubmittedNarrative { get; set; } = "";

    public string SubmittedByName { get; set; } = "";

    public bool HasCurrentWorkOrder =>
        !string.IsNullOrWhiteSpace(CurrentWorkOrder);
}