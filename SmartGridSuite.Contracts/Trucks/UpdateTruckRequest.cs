#nullable enable
namespace SmartGridSuite.Contracts.Trucks;

public sealed class UpdateTruckRequest
{
    public string? TruckNumber { get; set; }
    public int? TruckStyleId { get; set; }
    public bool? IsActive { get; set; }
}