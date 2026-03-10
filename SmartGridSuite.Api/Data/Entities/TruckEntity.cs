#nullable enable
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TruckEntity
    {
        public uint Id { get; set; }
        public string TruckNumber { get; set; } = "";

        public uint? TruckStyleId { get; set; }
        public virtual TruckStyleEntity? TruckStyle { get; set; }

        public string? DisplayName { get; set; } // legacy / compatibility only
        public bool IsActive { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual ICollection<TruckRosterEntity> TruckRosters { get; set; } = new List<TruckRosterEntity>();
        public virtual ICollection<TechnicianEntity> HomeTechnicians { get; set; } = new List<TechnicianEntity>();
    }
}