#nullable enable
namespace SmartGridSuite.Contracts.FieldTechnician;

public sealed class FieldTechTasksResponseDto
{
    public string TechnicianName { get; set; } = "";

    public List<FieldTechTicketListItemDto> DailyAssignments { get; set; } = new();

    public List<FieldTechTicketListItemDto> OtherAssignedTickets { get; set; } = new();
}