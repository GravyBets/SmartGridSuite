namespace SmartGridSuite.Contracts.Administration.Ticket.Status
{
    public class UpdateTicketStatusRequest
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public bool IsClosed { get; set; }
        public bool ShowInFilter { get; set; }
        public bool SendToDispatchTasks { get; set; }
    }
}