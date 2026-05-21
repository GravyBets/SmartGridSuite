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
        // Solo truck assignments may not have a CrewId because your current crew table
        // only creates CrewEntity rows when a truck has more than one tech.
        public uint? CrewId { get; set; }

        public int SortOrder { get; set; }

        public bool IsPublished { get; set; }
        public int PublishedVersion { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string? PublishedBy { get; set; }

        public ulong? CarriedFromAssignmentId { get; set; }

        public string? AssignmentNotes { get; set; }

        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "";

        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public TicketEntity? Ticket { get; set; }
        public TruckEntity? Truck { get; set; }
        public TechnicianEntity? Technician { get; set; }
        public CrewEntity? Crew { get; set; }

        public DailyTicketAssignmentEntity? CarriedFromAssignment { get; set; }
    }
}