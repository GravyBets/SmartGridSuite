#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities // <-- match your existing entities namespace
{
    public partial class TechnicianRosterEntity
    {
        public DateTime WorkDate { get; set; }   // MySQL DATE
        public uint TechnicianId { get; set; }
        public uint CrewId { get; set; }

        // Navigation (optional)
        public virtual CrewEntity? Crew { get; set; }
        public virtual TechnicianEntity? Technician { get; set; }
    }
}