#nullable enable
namespace SmartGridSuite.Contracts.FieldTechnician;

public sealed class FieldTechHistoryQueryResponse
{
    public string TechnicianName { get; set; } = "";

    public DateTime AppliedFrom { get; set; }

    public DateTime AppliedTo { get; set; }

    public List<FieldTechHistoryItemDto> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int ItemsWithWorkOrderCount { get; set; }

    public int ItemsWithoutWorkOrderCount { get; set; }
}