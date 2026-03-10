#nullable enable
namespace SmartGridSuite.Contracts.Crews;

public sealed class CrewDto
{
    public int Id { get; set; }
    public DateTime WorkDate { get; set; }          // date only, time ignored
    public string? TruckNumber { get; set; }
    public int? LeadTechnicianId { get; set; }
    public List<CrewMemberDto> Members { get; set; } = new();
}