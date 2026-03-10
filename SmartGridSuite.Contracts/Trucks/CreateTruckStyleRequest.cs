#nullable enable
namespace SmartGridSuite.Contracts.Trucks;

public sealed class CreateTruckStyleRequest
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}