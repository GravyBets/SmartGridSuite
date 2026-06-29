#nullable enable
namespace SmartGridSuite.Contracts.FieldTechnician;

public sealed class FieldTechHistoryQueryRequest
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public string? Search { get; set; }

    public int Skip { get; set; }

    public int Take { get; set; } = 500;
}