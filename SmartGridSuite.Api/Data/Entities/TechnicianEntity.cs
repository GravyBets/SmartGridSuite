#nullable enable
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TechnicianEntity
    {
        public uint Id { get; set; }
        public string EmployeeId { get; set; } = "";

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Title { get; set; } = "";

        public string? EmailAddress { get; set; }

        public bool IsActive { get; set; }

        public uint? HomeTruckId { get; set; }
        public virtual TruckEntity? HomeTruck { get; set; }

        public bool WorksMonday { get; set; }
        public bool WorksTuesday { get; set; }
        public bool WorksWednesday { get; set; }
        public bool WorksThursday { get; set; }
        public bool WorksFriday { get; set; }
        public bool WorksSaturday { get; set; }
        public bool WorksSunday { get; set; }

        public DateTime UpdatedAt { get; set; }

        public virtual ICollection<TechnicianRosterEntity> TechnicianRosters { get; set; } = new List<TechnicianRosterEntity>();
        public virtual ICollection<TechnicianRoleEntity> TechnicianRoles { get; set; } = new List<TechnicianRoleEntity>();
        public virtual ICollection<TechnicianWorkdayOverrideEntity> WorkdayOverrides { get; set; } = new List<TechnicianWorkdayOverrideEntity>();
    }
}