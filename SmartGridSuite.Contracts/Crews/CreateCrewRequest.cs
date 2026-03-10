#nullable enable
namespace SmartGridSuite.Contracts.Crews;

public sealed class CreateCrewRequest
{
    public DateTime? WorkDate { get; set; }     // null => today
    public string? TruckNumber { get; set; }
    public int? LeadTechnicianId { get; set; }
}