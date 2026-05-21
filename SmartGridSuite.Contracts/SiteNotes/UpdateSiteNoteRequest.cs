namespace SmartGridSuite.Contracts.SiteNotes
{
    public sealed class UpdateSiteNoteRequest
    {
        public ulong Id { get; set; }

        public string NoteType { get; set; } = "";
        public string NoteText { get; set; } = "";
        public string UpdatedBy { get; set; } = "";
    }
}