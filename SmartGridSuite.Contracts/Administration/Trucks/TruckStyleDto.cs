#nullable enable
using SmartGridSuite;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class TruckStyleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}