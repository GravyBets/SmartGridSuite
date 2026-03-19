namespace SmartGridSuite.Contracts.Administration
{
    public class UpdateTicketTaskCategoryRequest
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = "";
        public string DefaultActionRequired { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}