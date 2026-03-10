#nullable enable
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TruckStyleEntity
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual ICollection<TruckEntity> Trucks { get; set; } = new List<TruckEntity>();
    }
}