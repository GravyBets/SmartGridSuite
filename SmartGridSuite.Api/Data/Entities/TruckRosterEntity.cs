#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TruckRosterEntity
    {
        public DateTime WorkDate { get; set; }   // MySQL DATE
        public uint TechnicianId { get; set; }
        public uint TruckId { get; set; }

        public virtual TechnicianEntity? Technician { get; set; }
        public virtual TruckEntity? Truck { get; set; }
    }
}