using System;

namespace SmartGridSuite.Contracts.Dispatcher
{
    public class DispatchTaskListItemDto
    {
        public DateTime OccurredAt { get; set; }

        public string Site { get; set; } = "";
        public string Tech { get; set; } = "";

        public string Notification { get; set; } = "";
        public string WorkOrder { get; set; } = "";
        public string WorkOrderType { get; set; } = "";

        public string ActionRequired { get; set; } = "";
        public string Notes { get; set; } = "";

        public string Status { get; set; } = "";
        public string Category { get; set; } = "";
    }
}