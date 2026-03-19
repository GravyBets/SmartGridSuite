//Just column on the board for One truck + the tech inside it

#nullable enable
using SmartGridSuite;


//Just column on the board for One truck + the tech inside it

#nullable enable
using SmartGridSuite.Contracts.Administration.Technicians;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class TruckColumnDto
{
    public TruckDto Truck { get; set; } = new();
    public List<TechnicianDto> Technicians { get; set; } = new();
}