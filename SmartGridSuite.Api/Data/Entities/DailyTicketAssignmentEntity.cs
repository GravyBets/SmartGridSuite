#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class DailyTicketAssignmentEntity
    {
        public ulong Id { get; set; }

        public DateTime AssignmentDate { get; set; }

        public long TicketId { get; set; }

        // "Truck" or "Technician"
        public string TargetType { get; set; } = "";

        public uint? TruckId { get; set; }
        public uint? TechnicianId { get; set; }

        // Optional snapshot if the truck currently has a multi-tech crew.
        public uint? CrewId { get; set; }

        public int SortOrder { get; set; }

        public bool IsPublished { get; set; }
        public int PublishedVersion { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string? PublishedBy { get; set; }

        public ulong? CarriedFromAssignmentId { get; set; }

        public string? AssignmentNotes { get; set; }

        // Active, Completed, or Removed.
        public string AssignmentStatus { get; set; } = "Active";

        // Populated when successful write-up submission completes
        // this technician's current Daily Assignment.
        public DateTime? CompletedAt { get; set; }
        public string? CompletedBy { get; set; }
        public long? CompletedWriteUpSubmissionId { get; set; }

        // Populated when Dispatch explicitly withdraws the assignment.
        public DateTime? RemovedAt { get; set; }
        public string? RemovedBy { get; set; }

        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";

        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public TicketEntity? Ticket { get; set; }
        public TruckEntity? Truck { get; set; }
        public TechnicianEntity? Technician { get; set; }
        public CrewEntity? Crew { get; set; }

        public DailyTicketAssignmentEntity? CarriedFromAssignment { get; set; }

        public TicketWriteUpSubmissionEntity? CompletedWriteUpSubmission { get; set; }
    }
}