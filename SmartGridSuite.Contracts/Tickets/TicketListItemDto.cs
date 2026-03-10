using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartGridSuite.Contracts.Tickets
{
    public sealed record TicketListItemDto(
        long Id,
        string Site,
        string? NotificationName,
        string? Notification,
        string Status,
        string AssignedTech,
        DateTime CreatedAt,
        DateTime LastActivityAt,
        string? CurrentWorkOrder,
        string? WorkOrderClass, // "Maint" or "Cap"
        string GroupCode,      // HRM2/HRC1/...
        int PriorityDays,      // 1/3/5/15
        string Problem,
        string Notes,
        string CreatedBy
    );
}