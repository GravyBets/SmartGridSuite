#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class DailyTicketAssignmentPublishedEntity
    {
        public ulong Id { get; set; }

        public DateTime AssignmentDate { get; set; }
        public int PublishedVersion { get; set; }

        public long TicketId { get; set; }
        public ulong? SourceAssignmentId { get; set; }

        public string TargetType { get; set; } = "";

        public uint? TruckId { get; set; }
        public uint? TechnicianId { get; set; }
        public uint? CrewId { get; set; }

        public int SortOrder { get; set; }

        public string? AssignmentNotes { get; set; }

        public DateTime PublishedAt { get; set; }
        public string PublishedBy { get; set; } = "";

        public TicketEntity? Ticket { get; set; }
        public DailyTicketAssignmentEntity? SourceAssignment { get; set; }
        public TruckEntity? Truck { get; set; }
        public TechnicianEntity? Technician { get; set; }
        public CrewEntity? Crew { get; set; }
    }
}