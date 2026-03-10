#nullable enable
namespace SmartGridSuite.Api.Data.Entities
{
    public partial class TechnicianRoleEntity
    {
        public uint TechnicianId { get; set; }
        public uint RoleId { get; set; }

        public virtual TechnicianEntity Technician { get; set; } = null!;
        public virtual RoleEntity Role { get; set; } = null!;
    }
}