using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SmartGridSuite.Contracts.Tickets
{
    public sealed record CreateTicketRequest(
        string Site,
        string NotificationName,
        string Notification,
        string? WorkOrder,
        string WorkOrderClass,  // "Maint" or "Cap" (ignored if WorkOrder null/empty)
        string GroupCode,       // HRM2/HRC1/...
        int PriorityDays,       // 1/3/5/15
        string Status,
        string AssignedTech,
        string Problem,
        string Notes,
        string CreatedBy
    );
}