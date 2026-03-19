namespace SmartGridSuite.Contracts.Administration.Ticket.Status
{
    public class TicketTaskCategoryDto
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = "";
        public string DefaultActionRequired { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}