namespace SmartGridSuite.Contracts.Tickets;

public sealed class BulkTicketUpdateResponse
{
    public int RequestedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int NotFoundCount { get; set; }

    public int SkippedCount { get; set; }
}