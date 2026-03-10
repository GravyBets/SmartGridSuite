#nullable enable
namespace SmartGridSuite.Contracts.Crews;

public sealed class CrewMemberDto
{
    public int TechnicianId { get; set; }
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
}