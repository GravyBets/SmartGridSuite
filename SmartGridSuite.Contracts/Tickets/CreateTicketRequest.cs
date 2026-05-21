namespace SmartGridSuite.Contracts.Tickets
{
    public sealed record CreateTicketRequest(
        string Site,
        string NotificationName,
        string Notification,
        string? WorkOrder,
        string WorkOrderClass,
        string GroupCode,
        int PriorityDays,
        string Status,
        ulong? TaskCategoryId,
        string? ActionRequiredOverride,
        string AssignedTech,
        string Problem,
        string Notes,
        string CreatedBy,
        string? DispatchNotes = null
    );
}