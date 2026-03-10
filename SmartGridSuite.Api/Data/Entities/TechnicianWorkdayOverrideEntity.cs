#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TechnicianWorkdayOverrideEntity
    {
        public DateTime WorkDate { get; set; }   // date only
        public uint TechnicianId { get; set; }
        public bool IsWorking { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual TechnicianEntity Technician { get; set; } = null!;
    }
}