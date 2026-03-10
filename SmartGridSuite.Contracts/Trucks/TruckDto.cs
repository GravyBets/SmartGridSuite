#nullable enable
namespace SmartGridSuite.Contracts.Trucks;

public sealed class TruckDto
{
    public int Id { get; set; }
    public string TruckNumber { get; set; } = "";

    public int? TruckStyleId { get; set; }
    public string? TruckStyleName { get; set; }

    public bool IsActive { get; set; }

    // compatibility field for older client bits during transition
    public string? DisplayName { get; set; }
}