using System;

namespace SmartGridSuite.Contracts.Dispatcher
{
    public sealed class DispatchTaskListItemDto
    {
        public long TicketId { get; set; }

        public DateTime OccurredAt { get; set; }

        public string Site { get; set; } = "";
        public string NotificationName { get; set; } = "";
        public string Problem { get; set; } = "";

        public string Tech { get; set; } = "";

        public string Notification { get; set; } = "";
        public string WorkOrder { get; set; } = "";
        public string WorkOrderType { get; set; } = "";

        public string ActionRequired { get; set; } = "";
        public string Notes { get; set; } = "";

        public string Status { get; set; } = "";

        // Temporary compatibility field while TaskPaneView is being rebuilt.
        // New Tasks UI will no longer expose legacy task categories.
        public string Category { get; set; } = "";
    }
}