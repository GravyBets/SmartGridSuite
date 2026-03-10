#nullable enable
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities // <-- match your existing entities namespace
{
    public partial class CrewEntity
    {
        public uint Id { get; set; }
        public DateTime WorkDate { get; set; }          // MySQL DATE (time ignored)
        public string? TruckNumber { get; set; }
        public uint? LeadTechnicianId { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation (optional but useful)
        public virtual ICollection<TechnicianRosterEntity> TechnicianRosters { get; set; } = new List<TechnicianRosterEntity>();
        public virtual ICollection<TicketEntity> Tickets { get; set; } = new List<TicketEntity>();
    }
}