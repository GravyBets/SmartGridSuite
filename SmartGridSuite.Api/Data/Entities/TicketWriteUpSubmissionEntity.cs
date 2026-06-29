#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public class TicketWriteUpSubmissionEntity
    {
        public long Id { get; set; }

        public long TicketId { get; set; }

        public long? SiteHistoryId { get; set; }

        // Stores the client-generated idempotency key used to prevent duplicate
        // Site History and write-up records when an uncertain submission is retried.
        public Guid? ClientSubmissionId { get; set; }

        public uint? SubmittedByTechnicianId { get; set; }

        public string SubmittedByEmployeeId { get; set; } = "";

        public string SubmittedByName { get; set; } = "";

        public DateTime SubmittedAt { get; set; }

        public string SubmittedNarrative { get; set; } = "";

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAt { get; set; }

        public string? DeletedBy { get; set; }

        public virtual TicketEntity? Ticket { get; set; }

        public virtual TechnicianEntity? SubmittedByTechnician { get; set; }

        public DateTime? EditedAt { get; set; }

        public string? EditedBy { get; set; }
    }
}