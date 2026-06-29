#nullable enable
namespace SmartGridSuite.Contracts.FieldTechnician;

public sealed class FieldTechExpandedTicketDetailsDto
{
    public long TicketId { get; set; }

    public string Site { get; set; } = "";

    public string DispatchNotes { get; set; } = "";

    public List<FieldTechSiteNoteDto> SiteNotes { get; set; } = new();
}

public sealed class FieldTechSiteNoteDto
{
    public ulong Id { get; set; }

    public string NoteType { get; set; } = "";

    public string NoteText { get; set; } = "";

    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public string UpdatedBy { get; set; } = "";

    public DateTime? UpdatedAt { get; set; }
}