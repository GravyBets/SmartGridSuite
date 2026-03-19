#nullable enable
using SmartGridSuite;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class CreateTruckStyleRequest
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}