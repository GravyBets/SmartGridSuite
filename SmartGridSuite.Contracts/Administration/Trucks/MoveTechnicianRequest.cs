//WPF drag/drop will call this

#nullable enable
using SmartGridSuite;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class MoveTechnicianRequest
{
    public DateTime WorkDate { get; set; }                 // date only
    public int TechnicianId { get; set; }
    public int? ToTruckId { get; set; }                    // null => Unassigned
}