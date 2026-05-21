namespace SmartGridSuite.Api.Data.Entities
{
    public class SiteNoteEntity
    {
        public ulong Id { get; set; }

        public string SiteId { get; set; } = "";
        public string? NoteType { get; set; }
        public string NoteText { get; set; } = "";

        public bool IsActive { get; set; }

        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public string? DeletedBy { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}