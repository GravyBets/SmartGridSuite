using SmartGridSuite.Contracts.Tickets;

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

        public long? SubmissionId { get; set; }

        public DateTime? SubmittedAt { get; set; }

        public string SubmittedByName { get; set; } = "";

        public string SubmittedWriteUp { get; set; } = "";

        public List<string> WriteUpFlags { get; set; } = new();

        public List<string> ReferToOptions { get; set; } = new();

        public List<DispatchCloseoutChecklistItemDto>
            CloseoutChecklistItems
        { get; set; } = new();

        public int RequiredChecklistRemaining { get; set; }

        public bool CanMarkClosed { get; set; } = true;

        // Temporary compatibility field while TaskPaneView is rebuilt.
        // New Tasks UI will no longer expose legacy task categories.
        public string Category { get; set; } = "";
    }
}