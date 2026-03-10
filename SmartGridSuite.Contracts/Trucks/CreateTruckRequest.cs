#nullable enable
namespace SmartGridSuite.Contracts.Trucks;

public sealed class CreateTruckRequest
{
    public string TruckNumber { get; set; } = "";
    public int? TruckStyleId { get; set; }
    public bool IsActive { get; set; } = true;
}