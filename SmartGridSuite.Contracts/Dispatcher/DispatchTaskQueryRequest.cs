namespace SmartGridSuite.Contracts.Dispatcher
{
    public sealed class DispatchTaskQueryRequest
    {
        public string? Search { get; set; }

        public List<string> Statuses { get; set; } = new();

        public bool ApplyStatusFilter { get; set; }

        public string? AssignedTech { get; set; }

        public DateTime? From { get; set; }

        public DateTime? To { get; set; }

        public int Skip { get; set; }

        public int Take { get; set; } = 500;
    }
}