namespace SmartGridSuite.Contracts.Tickets
{
    public sealed class SubmitTicketWriteUpRequest
    {
        public string FinalWriteUpText { get; set; } = "";
        public string SubmittedBy { get; set; } = "Unknown";
    }
}