namespace SmartGridSuite.Contracts.Administration.Ticket.Status
{
    public class CreateTicketStatusRequest
    {
        public string Name { get; set; } = "";

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsClosed { get; set; }

        public bool IsFieldComplete { get; set; }

        public bool ShowInFilter { get; set; } = true;

        public bool SendToDispatchTasks { get; set; }

        public bool IncludeInSummary { get; set; } = true;

        public bool IsWriteUpSubmitTarget { get; set; }
    }
}