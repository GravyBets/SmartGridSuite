//whole board for a date

#nullable enable
using SmartGridSuite.Contracts.Technicians;

namespace SmartGridSuite.Contracts.Trucks;

public sealed class TruckBoardDto
{
    public DateTime WorkDate { get; set; }                 // date only
    public List<TruckColumnDto> Trucks { get; set; } = new();
    public List<TechnicianDto> Unassigned { get; set; } = new();
}