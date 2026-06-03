namespace SmartGridSuite.Contracts.Dispatcher
{
    public sealed class DispatchTaskQueryResponse
    {
        public List<DispatchTaskListItemDto> Items { get; set; } = new();

        public int TotalCount { get; set; }
    }
}