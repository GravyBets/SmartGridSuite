#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TechnicianRosterEntity
    {
        public DateTime WorkDate { get; set; }
        public uint TechnicianId { get; set; }
        public uint CrewId { get; set; }

        public virtual TechnicianEntity? Technician { get; set; }
        public virtual CrewEntity? Crew { get; set; }
    }
}