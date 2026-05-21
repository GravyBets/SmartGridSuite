namespace SmartGridSuite.Contracts.SiteNotes
{
    public sealed class CreateSiteNoteRequest
    {
        public string SiteId { get; set; } = "";
        public string NoteType { get; set; } = "";
        public string NoteText { get; set; } = "";
        public string CreatedBy { get; set; } = "";
    }
}