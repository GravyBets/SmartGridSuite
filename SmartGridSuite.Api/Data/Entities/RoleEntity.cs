#nullable enable
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class RoleEntity
    {
        public uint Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";

        public virtual ICollection<TechnicianRoleEntity> TechnicianRoles { get; set; } = new List<TechnicianRoleEntity>();
    }
}