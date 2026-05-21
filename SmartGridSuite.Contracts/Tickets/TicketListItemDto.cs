using System;

namespace SmartGridSuite.Contracts.Tickets
{
    public sealed record TicketListItemDto(
        long Id,
        string Site,
        string? NotificationName,
        string? Notification,
        string Status,
        ulong? TaskCategoryId,
        string? TaskCategoryName,
        string? ActionRequiredOverride,
        string AssignedTech,
        DateTime CreatedAt,
        DateTime LastActivityAt,
        string? CurrentWorkOrder,
        string? WorkOrderClass,
        string GroupCode,
        int PriorityDays,
        string Problem,
        string Notes,
        string CreatedBy,
        string DispatchNotes = ""
    );
}