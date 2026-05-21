namespace SmartGridSuite.Contracts.Tickets
{
    public sealed record UpdateTicketRequest(
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
        string? DispatchNotes = null
    );
}