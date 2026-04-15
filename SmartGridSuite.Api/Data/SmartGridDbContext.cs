using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data.Entities;

namespace SmartGridSuite.Api.Data
{
    public class SmartGridDbContext : DbContext
    {
        public SmartGridDbContext(DbContextOptions<SmartGridDbContext> options) : base(options) { }

        public DbSet<TicketEntity> Tickets => Set<TicketEntity>();
        public virtual DbSet<CrewEntity> Crews { get; set; }
        public virtual DbSet<TechnicianRosterEntity> TechnicianRosters { get; set; }
        public virtual DbSet<TechnicianEntity> Technicians { get; set; }

        public virtual DbSet<RoleEntity> Roles { get; set; }
        public virtual DbSet<TechnicianRoleEntity> TechnicianRoles { get; set; }
        public virtual DbSet<TechnicianWorkdayOverrideEntity> TechnicianWorkdayOverrides { get; set; }
        public virtual DbSet<TruckEntity> Trucks { get; set; }
        public virtual DbSet<TruckRosterEntity> TruckRosters { get; set; }        
        public virtual DbSet<TruckStyleEntity> TruckStyles { get; set; }

        public virtual DbSet<SnmpProfileEntity> SnmpProfiles { get; set; } = null!;
        public virtual DbSet<SnmpOidEntity> SnmpOids { get; set; } = null!;
        public virtual DbSet<SnmpOidDecodeValueEntity> SnmpOidDecodeValues { get; set; } = null!;

        public DbSet<TicketStatusEntity> TicketStatuses { get; set; }

        public DbSet<TicketTaskCategoryEntity> TicketTaskCategories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TicketEntity>(e =>
            {
                e.ToTable("tickets");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Site).HasColumnName("site");
                e.Property(x => x.Notification).HasColumnName("notification").HasMaxLength(10).IsRequired(false);
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.AssignedTech).HasColumnName("assigned_tech");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.LastActivityAt).HasColumnName("last_activity_at");
                e.Property(x => x.CurrentWorkOrder).HasColumnName("current_work_order");
                e.Property(x => x.WorkOrderClass).HasColumnName("work_order_class");
                e.Property(x => x.Summary).HasColumnName("summary");
                e.Property(x => x.NotificationName).HasColumnName("notification_name");
                e.Property(x => x.GroupCode).HasColumnName("group_code");
                e.Property(x => x.PriorityDays).HasColumnName("priority_days");
                e.Property(x => x.Problem).HasColumnName("problem");
                e.Property(x => x.Notes).HasColumnName("notes");
                e.Property(x => x.CreatedBy).HasColumnName("created_by");
                e.Property(x => x.AssignedCrewId).HasColumnName("assigned_crew_id");

                e.HasIndex(x => x.AssignedCrewId).HasDatabaseName("ix_tickets_assigned_crew");

                e.Property(x => x.TaskCategoryId).HasColumnName("task_category_id");
                e.Property(x => x.ActionRequiredOverride).HasColumnName("action_required_override").HasMaxLength(255);
                
                e.HasOne(x => x.TaskCategory).WithMany().HasForeignKey(x => x.TaskCategoryId).OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.AssignedCrew)
                 .WithMany(c => c.Tickets)
                 .HasForeignKey(x => x.AssignedCrewId)
                 .OnDelete(DeleteBehavior.SetNull);

            });

            modelBuilder.Entity<CrewEntity>(e =>
            {
                e.ToTable("crews");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date");
                e.Property(x => x.TruckNumber).HasColumnName("truck_number").HasMaxLength(16);
                e.Property(x => x.LeadTechnicianId).HasColumnName("lead_technician_id");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                // Optional relationship to Technician lead (safe even if null)
                e.HasOne<TechnicianEntity>()
                 .WithMany()
                 .HasForeignKey(x => x.LeadTechnicianId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<TechnicianRosterEntity>(e =>
            {
                e.ToTable("technician_roster");
                e.HasKey(x => new { x.WorkDate, x.TechnicianId });

                e.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date");
                e.Property(x => x.TechnicianId).HasColumnName("technician_id");
                e.Property(x => x.CrewId).HasColumnName("crew_id");

                e.HasIndex(x => new { x.CrewId, x.WorkDate }).HasDatabaseName("ix_roster_crew");

                e.HasOne(x => x.Technician)
                 .WithMany(t => t.TechnicianRosters)
                 .HasForeignKey(x => x.TechnicianId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Technician)
                 .WithMany()
                 .HasForeignKey(x => x.TechnicianId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TechnicianEntity>(e =>
            {
                e.ToTable("technicians");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.EmployeeId).HasColumnName("employee_id").HasMaxLength(32).IsRequired();

                e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(64).IsRequired();
                e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(64).IsRequired();
                e.Property(x => x.Title).HasColumnName("Title").HasMaxLength(32);

                e.Property(x => x.IsActive).HasColumnName("is_active");

                e.Property(x => x.HomeTruckId).HasColumnName("home_truck_id");

                e.Property(x => x.WorksMonday).HasColumnName("works_monday");
                e.Property(x => x.WorksTuesday).HasColumnName("works_tuesday");
                e.Property(x => x.WorksWednesday).HasColumnName("works_wednesday");
                e.Property(x => x.WorksThursday).HasColumnName("works_thursday");
                e.Property(x => x.WorksFriday).HasColumnName("works_friday");
                e.Property(x => x.WorksSaturday).HasColumnName("works_saturday");
                e.Property(x => x.WorksSunday).HasColumnName("works_sunday");

                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                e.HasIndex(x => x.EmployeeId).IsUnique().HasDatabaseName("uq_technicians_employee_id");
                e.HasIndex(x => x.HomeTruckId).HasDatabaseName("ix_technicians_home_truck");

                e.HasOne(x => x.HomeTruck)
                 .WithMany(t => t.HomeTechnicians)
                 .HasForeignKey(x => x.HomeTruckId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<TruckEntity>(e =>
            {
                e.ToTable("trucks");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.TruckNumber).HasColumnName("truck_number").HasMaxLength(16).IsRequired();

                e.Property(x => x.TruckStyleId).HasColumnName("truck_style_id");

                e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(64);
                e.Property(x => x.IsActive).HasColumnName("is_active");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                e.HasIndex(x => x.TruckNumber).IsUnique().HasDatabaseName("uq_trucks_truck_number");
                e.HasIndex(x => x.TruckStyleId).HasDatabaseName("ix_trucks_truck_style");

                e.HasOne(x => x.TruckStyle)
                 .WithMany(s => s.Trucks)
                 .HasForeignKey(x => x.TruckStyleId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<TruckRosterEntity>(e =>
            {
                e.ToTable("truck_roster");
                e.HasKey(x => new { x.WorkDate, x.TechnicianId });

                e.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date");
                e.Property(x => x.TruckId).HasColumnName("truck_id");
                e.Property(x => x.TechnicianId).HasColumnName("technician_id");

                e.HasIndex(x => new { x.WorkDate, x.TruckId }).HasDatabaseName("ix_truck_roster_truck");

                e.HasOne(x => x.Truck)
                 .WithMany(t => t.TruckRosters)
                 .HasForeignKey(x => x.TruckId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Technician)
                 .WithMany() // keep simple for now
                 .HasForeignKey(x => x.TechnicianId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TruckStyleEntity>(e =>
            {
                e.ToTable("truck_styles");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
                e.Property(x => x.IsActive).HasColumnName("is_active");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("uq_truck_styles_name");
            });

            modelBuilder.Entity<RoleEntity>(e =>
            {
                e.ToTable("roles");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();

                e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_roles_code");
            });

            modelBuilder.Entity<TechnicianRoleEntity>(e =>
            {
                e.ToTable("technician_roles");
                e.HasKey(x => new { x.TechnicianId, x.RoleId });

                e.Property(x => x.TechnicianId).HasColumnName("technician_id");
                e.Property(x => x.RoleId).HasColumnName("role_id");

                e.HasIndex(x => x.RoleId).HasDatabaseName("ix_technician_roles_role");

                e.HasOne(x => x.Technician)
                 .WithMany(t => t.TechnicianRoles)
                 .HasForeignKey(x => x.TechnicianId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Role)
                 .WithMany(r => r.TechnicianRoles)
                 .HasForeignKey(x => x.RoleId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TechnicianWorkdayOverrideEntity>(e =>
            {
                e.ToTable("technician_workday_overrides");
                e.HasKey(x => new { x.WorkDate, x.TechnicianId });

                e.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date");
                e.Property(x => x.TechnicianId).HasColumnName("technician_id");
                e.Property(x => x.IsWorking).HasColumnName("is_working");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                e.HasIndex(x => x.TechnicianId).HasDatabaseName("ix_workday_overrides_technician");

                e.HasOne(x => x.Technician)
                 .WithMany(t => t.WorkdayOverrides)
                 .HasForeignKey(x => x.TechnicianId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SnmpProfileEntity>(e =>
            {
                e.ToTable("snmp_profiles");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
                e.Property(x => x.DeviceFamily).HasColumnName("device_family").HasMaxLength(50).IsRequired();

                e.Property(x => x.IsActive).HasColumnName("is_active");
                e.Property(x => x.IsDefaultForFamily).HasColumnName("is_default_for_family");

                e.Property(x => x.ReadCommunity).HasColumnName("read_community").HasMaxLength(255);
                e.Property(x => x.WriteCommunity).HasColumnName("write_community").HasMaxLength(255);
                e.Property(x => x.ContextName).HasColumnName("context_name").HasMaxLength(255);

                e.Property(x => x.UsmUser).HasColumnName("usm_user").HasMaxLength(255);
                e.Property(x => x.AuthProtocol).HasColumnName("auth_protocol").HasMaxLength(20);
                e.Property(x => x.AuthKey).HasColumnName("auth_key").HasMaxLength(255);
                e.Property(x => x.PrivacyProtocol).HasColumnName("privacy_protocol").HasMaxLength(20);
                e.Property(x => x.PrivacyKey).HasColumnName("privacy_key").HasMaxLength(255);

                e.Property(x => x.TimeoutMs).HasColumnName("timeout_ms");
                e.Property(x => x.Retries).HasColumnName("retries");

                e.Property(x => x.IsDeleted).HasColumnName("is_deleted");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                e.HasMany(x => x.Oids)
                    .WithOne(x => x.SnmpProfile)
                    .HasForeignKey(x => x.SnmpProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SnmpOidEntity>(e =>
            {
                e.ToTable("snmp_oids");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.SnmpProfileId).HasColumnName("snmp_profile_id");

                e.Property(x => x.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
                e.Property(x => x.Label).HasColumnName("label").HasMaxLength(100).IsRequired();
                e.Property(x => x.Oid).HasColumnName("oid").HasMaxLength(255).IsRequired();
                e.Property(x => x.ValueType).HasColumnName("value_type").HasMaxLength(30).IsRequired();

                e.Property(x => x.IsWritable).HasColumnName("is_writable");
                e.Property(x => x.ShowInWorkspace).HasColumnName("show_in_workspace");
                e.Property(x => x.SortOrder).HasColumnName("sort_order");

                e.Property(x => x.IsDeleted).HasColumnName("is_deleted");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

                e.Property(x => x.DecodeMode).HasColumnName("decode_mode").HasMaxLength(30).IsRequired();
                e.Property(x => x.ShowRawValueAlongsideDecoded).HasColumnName("show_raw_value_alongside_decoded");

                e.HasMany(x => x.DecodeValues)
                    .WithOne(x => x.SnmpOid)
                    .HasForeignKey(x => x.SnmpOidId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SnmpOidDecodeValueEntity>(e =>
            {
                e.ToTable("snmp_oid_decode_values");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.SnmpOidId).HasColumnName("snmp_oid_id");

                e.Property(x => x.RawValue).HasColumnName("raw_value").HasMaxLength(100).IsRequired();
                e.Property(x => x.DisplayText).HasColumnName("display_text").HasMaxLength(255).IsRequired();
                e.Property(x => x.SortOrder).HasColumnName("sort_order");

                e.Property(x => x.IsDeleted).HasColumnName("is_deleted");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            });
        }
    }
}