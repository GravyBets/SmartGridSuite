namespace SmartGridSuite.Contracts.Tickets
{
    public sealed class TicketActionReasonRequest
    {
        public string Reason { get; set; } = "";
        public string RequestedBy { get; set; } = "Unknown";
    }
}