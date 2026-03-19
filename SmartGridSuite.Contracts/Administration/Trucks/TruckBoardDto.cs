//whole board for a date

#nullable enable
using SmartGridSuite;


//whole board for a date

#nullable enable
using SmartGridSuite.Contracts.Administration.Technicians;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class TruckBoardDto
{
    public DateTime WorkDate { get; set; }                 // date only
    public List<TruckColumnDto> Trucks { get; set; } = new();
    public List<TechnicianDto> Unassigned { get; set; } = new();
}