#nullable enable

namespace SmartGridSuite.Contracts.Tickets
{
    public sealed class ResolveSiteTicketRequest
    {
        public string Site { get; set; } = "";

        public string EmployeeId { get; set; } = "";

        /*
         * Supplied when the dashboard already has an authoritative
         * ticket context, such as opening a ticket from My Tasks or
         * after the technician explicitly chooses a ticket.
         */
        public long? ExplicitTicketId { get; set; }
    }

    public sealed class ResolveSiteTicketResponse
    {
        /*
         * Possible values:
         *
         * Resolved
         * NoActiveTicket
         * ChoiceRequired
         */
        public string Resolution { get; set; } = "";

        public long? TicketId { get; set; }

        /*
         * True when technician assignment was used to narrow the
         * candidate set before applying the oldest-created rule.
         */
        public bool UsedTechnicianAssignment { get; set; }

        public string Message { get; set; } = "";

        /*
         * Populated when Resolution == "ChoiceRequired".
         */
        public List<SiteTicketResolutionCandidateDto> Candidates { get; set; } =
            new();
    }

    public sealed class SiteTicketResolutionCandidateDto
    {
        public long TicketId { get; set; }

        public string Site { get; set; } = "";

        public string NotificationName { get; set; } = "";

        public string Notification { get; set; } = "";

        public string WorkOrder { get; set; } = "";

        public string WorkOrderClass { get; set; } = "";

        public string Status { get; set; } = "";

        public string AssignedTech { get; set; } = "";

        public string Problem { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public string CreatedBy { get; set; } = "";
    }
}