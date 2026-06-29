namespace SmartGridSuite.Contracts.Tickets;

public sealed class SapQueueImportLastImportDto
{
    public DateTime? ImportedAt { get; set; }

    public string ImportedBy { get; set; } = "";

    public int ImportedCount { get; set; }
}