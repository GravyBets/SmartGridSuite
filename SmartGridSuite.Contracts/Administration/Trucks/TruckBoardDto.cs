#nullable enable
using SmartGridSuite.Contracts.Administration.Technicians;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class TruckBoardDto
{
    public DateTime WorkDate { get; set; }
    public List<TruckColumnDto> Trucks { get; set; } = new();
    public List<TechnicianDto> Unassigned { get; set; } = new();

    // Full right-side technician drawer source.
    public List<TechnicianDto> AllTechnicians { get; set; } = new();
}