namespace SmartGridSuite.Contracts.Administration
{
    public class CreateTicketTaskCategoryRequest
    {
        public string Name { get; set; } = "";
        public string DefaultActionRequired { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}