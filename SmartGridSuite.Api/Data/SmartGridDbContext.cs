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
        public virtual DbSet<TruckBoardDayEntity> TruckBoardDays { get; set; }
        public virtual DbSet<TruckStyleEntity> TruckStyles { get; set; }

        public virtual DbSet<SnmpProfileEntity> SnmpProfiles { get; set; } = null!;
        public virtual DbSet<SnmpOidEntity> SnmpOids { get; set; } = null!;
        public virtual DbSet<SnmpOidDecodeValueEntity> SnmpOidDecodeValues { get; set; } = null!;

        public DbSet<TicketStatusEntity> TicketStatuses { get; set; }
        public DbSet<TicketTaskCategoryEntity> TicketTaskCategories { get; set; }

        public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

        public DbSet<EmailLogEntity> EmailLogs => Set<EmailLogEntity>();

        public DbSet<SiteNoteEntity> SiteNotes => Set<SiteNoteEntity>();

        public DbSet<SiteHistoryEntity> SiteHistory => Set<SiteHistoryEntity>();

        //CACHED SITE STUFF
        public DbSet<CacheSiteEntity> CacheSites => Set<CacheSiteEntity>();

        public DbSet<CacheSiteAmsEntity> CacheSiteAms => Set<CacheSiteAmsEntity>();

        public DbSet<CacheSiteAddressEntity> CacheSiteAddresses => Set<CacheSiteAddressEntity>();

        public DbSet<CacheSiteGpsEntity> CacheSiteGps => Set<CacheSiteGpsEntity>();

        public DbSet<CacheSiteLteEntity> CacheSiteLte => Set<CacheSiteLteEntity>();

        public DbSet<CacheSitePmrEntity> CacheSitePmr => Set<CacheSitePmrEntity>();

        public DbSet<CacheSiteTopEntity> CacheSiteTop => Set<CacheSiteTopEntity>();

        public DbSet<CacheSiteDacsEntity> CacheSiteDacs => Set<CacheSiteDacsEntity>();

        public DbSet<CacheSiteIgsdEntity> CacheSiteIgsd => Set<CacheSiteIgsdEntity>();

        public DbSet<CacheSiteRxEntity> CacheSiteRx => Set<CacheSiteRxEntity>();

        public DbSet<CacheTowerEntity> CacheTowers => Set<CacheTowerEntity>();

        public DbSet<CacheTowerSectorEntity> CacheTowerSectors => Set<CacheTowerSectorEntity>();

        //END OF CACHED SITE STUFF

        public DbSet<TicketWriteUpSubmissionEntity> TicketWriteUpSubmissions => Set<TicketWriteUpSubmissionEntity>();

        public DbSet<TicketWriteUpSubmissionTechnicianEntity> TicketWriteUpSubmissionTechnicians
        => Set<TicketWriteUpSubmissionTechnicianEntity>();

        public virtual DbSet<CommunicationDeviceTypeEntity> CommunicationDeviceTypes { get; set; }

        public DbSet<DailyTicketAssignmentEntity> DailyTicketAssignments => Set<DailyTicketAssignmentEntity>();

        public DbSet<DailyTicketAssignmentPublishedEntity> DailyTicketAssignmentPublished => Set<DailyTicketAssignmentPublishedEntity>();
        
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
                e.Property(x => x.DispatchNotes).HasColumnName("dispatch_notes");
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

                e.Property(x => x.WorkDate)
                    .HasColumnName("work_date")
                    .HasColumnType("date");

                e.Property(x => x.TechnicianId)
                    .HasColumnName("technician_id");

                e.Property(x => x.CrewId)
                    .HasColumnName("crew_id");

                e.HasIndex(x => x.CrewId)
                    .HasDatabaseName("ix_technician_roster_crew");

                e.HasOne(x => x.Technician)
                    .WithMany(t => t.TechnicianRosters)
                    .HasForeignKey(x => x.TechnicianId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Crew)
                    .WithMany(c => c.TechnicianRosters)
                    .HasForeignKey(x => x.CrewId)
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

                e.Property(x => x.EmailAddress).HasColumnName("email_address").HasMaxLength(255);

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

            modelBuilder.Entity<TruckBoardDayEntity>(e =>
            {
                e.ToTable("truck_board_days");

                e.HasKey(x => x.WorkDate);

                e.Property(x => x.WorkDate)
                    .HasColumnName("work_date")
                    .HasColumnType("date");

                e.Property(x => x.InitializationSource)
                    .HasColumnName("initialization_source")
                    .HasMaxLength(32)
                    .IsRequired();

                e.Property(x => x.CarriedFromWorkDate)
                    .HasColumnName("carried_from_work_date")
                    .HasColumnType("date");

                e.Property(x => x.InitializedAt)
                    .HasColumnName("initialized_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.CarriedFromWorkDate)
                    .HasDatabaseName("ix_truck_board_days_carried_from_date");
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

                e.Property(x => x.SnmpVersion).HasColumnName("snmp_version").HasMaxLength(10).IsRequired();

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

                // Formula decode fields.
                // These columns live on snmp_oids and are configured per OID.
                e.Property(x => x.ReadFormula).HasColumnName("read_formula").HasMaxLength(100);
                e.Property(x => x.WriteFormula).HasColumnName("write_formula").HasMaxLength(100);
                e.Property(x => x.DecimalPlaces).HasColumnName("decimal_places");

                e.Property(x => x.UnitLabel).HasColumnName("unit_label").HasMaxLength(20);

                e.HasMany(x => x.DecodeValues).WithOne(x => x.SnmpOid).HasForeignKey(x => x.SnmpOidId).OnDelete(DeleteBehavior.Cascade);
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

            modelBuilder.Entity<AppSettingEntity>(e =>
            {
                e.ToTable("app_settings");
                e.HasKey(x => x.SettingKey);

                e.Property(x => x.SettingKey)
                    .HasColumnName("setting_key")
                    .HasMaxLength(100);

                e.Property(x => x.SettingValue)
                    .HasColumnName("setting_value");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");
            });

            modelBuilder.Entity<EmailLogEntity>(e =>
            {
                e.ToTable("email_logs");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.EmailType)
                    .HasColumnName("email_type")
                    .HasMaxLength(64)
                    .IsRequired();

                e.Property(x => x.EnabledAtSendTime)
                    .HasColumnName("enabled_at_send_time")
                    .HasColumnType("tinyint(1)");

                e.Property(x => x.DryRun)
                    .HasColumnName("dry_run")
                    .HasColumnType("tinyint(1)");

                e.Property(x => x.FromAddress)
                    .HasColumnName("from_address")
                    .HasMaxLength(255)
                    .IsRequired();

                e.Property(x => x.FromDisplayName)
                    .HasColumnName("from_display_name")
                    .HasMaxLength(255);

                e.Property(x => x.ToAddresses)
                    .HasColumnName("to_addresses")
                    .IsRequired();

                e.Property(x => x.CcAddresses)
                    .HasColumnName("cc_addresses");

                e.Property(x => x.BccAddresses)
                    .HasColumnName("bcc_addresses");

                e.Property(x => x.Subject)
                    .HasColumnName("subject")
                    .HasMaxLength(255)
                    .IsRequired();

                e.Property(x => x.BodyPreview)
                    .HasColumnName("body_preview");

                e.Property(x => x.Status)
                    .HasColumnName("status")
                    .HasMaxLength(32)
                    .IsRequired();

                e.Property(x => x.ErrorMessage)
                    .HasColumnName("error_message");

                e.Property(x => x.RelatedTicketId)
                    .HasColumnName("related_ticket_id");

                e.Property(x => x.RelatedSite)
                    .HasColumnName("related_site")
                    .HasMaxLength(64);

                e.Property(x => x.CreatedBy)
                    .HasColumnName("created_by")
                    .HasMaxLength(100);

                e.HasIndex(x => x.CreatedAt)
                    .HasDatabaseName("ix_email_logs_created_at");

                e.HasIndex(x => x.Status)
                    .HasDatabaseName("ix_email_logs_status");

                e.HasIndex(x => x.EmailType)
                    .HasDatabaseName("ix_email_logs_email_type");

                e.HasIndex(x => x.RelatedTicketId)
                    .HasDatabaseName("ix_email_logs_related_ticket");
            });

            modelBuilder.Entity<CommunicationDeviceTypeEntity>(entity =>
            {
                entity.ToTable("communication_device_types");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                    .HasColumnName("id");

                entity.Property(e => e.DisplayName)
                    .HasColumnName("display_name")
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(e => e.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(true);

                entity.Property(e => e.SortOrder)
                    .HasColumnName("sort_order")
                    .HasDefaultValue(0);

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at");

                entity.Property(e => e.UpdatedAt)
                    .HasColumnName("updated_at");

                entity.HasIndex(e => e.DisplayName)
                    .IsUnique()
                    .HasDatabaseName("ux_communication_device_types_display_name");
            });

            modelBuilder.Entity<SiteNoteEntity>(e =>
            {
                e.ToTable("site_notes");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(64)
                    .IsRequired();

                e.Property(x => x.NoteType)
                    .HasColumnName("note_type")
                    .HasMaxLength(50);

                e.Property(x => x.NoteText)
                    .HasColumnName("note_text")
                    .IsRequired();

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active");

                e.Property(x => x.CreatedBy)
                    .HasColumnName("created_by")
                    .HasMaxLength(100)
                    .IsRequired();

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedBy)
                    .HasColumnName("updated_by")
                    .HasMaxLength(100);

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.Property(x => x.DeletedBy)
                    .HasColumnName("deleted_by")
                    .HasMaxLength(100);

                e.Property(x => x.DeletedAt)
                    .HasColumnName("deleted_at");

                e.HasIndex(x => x.SiteId)
                    .HasDatabaseName("ix_site_notes_site_id");

                e.HasIndex(x => x.IsActive)
                    .HasDatabaseName("ix_site_notes_active");

                e.HasIndex(x => new { x.SiteId, x.IsActive })
                    .HasDatabaseName("ix_site_notes_site_active");

                e.Property(x => x.DeletedAt)
                    .HasColumnName("deleted_at");

                e.Property(x => x.DeletedBy)
                    .HasColumnName("deleted_by")
                    .HasMaxLength(100);
            });

            // Maps the existing site-history table so submitted write-ups can be inserted
            // through EF and their generated history IDs can be linked to completion records.
            modelBuilder.Entity<SiteHistoryEntity>(e =>
            {
                e.ToTable("site_history");

                e.HasKey(x => x.HistoryId);

                e.Property(x => x.HistoryId)
                    .HasColumnName("history_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.LegacySourceId)
                    .HasColumnName("legacy_source_id");

                e.Property(x => x.SourceType)
                    .HasColumnName("source_type")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceFile)
                    .HasColumnName("source_file")
                    .HasMaxLength(255);

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.VisitDate)
                    .HasColumnName("visit_date")
                    .HasColumnType("date");

                e.Property(x => x.PrimaryTech)
                    .HasColumnName("primary_tech")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryTech)
                    .HasColumnName("secondary_tech")
                    .HasMaxLength(100);

                e.Property(x => x.Narrative)
                    .HasColumnName("narrative");

                e.Property(x => x.IssueText)
                    .HasColumnName("issue_text");

                e.Property(x => x.ImportedAt)
                    .HasColumnName("imported_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAdd();

                e.HasIndex(x => x.SiteId)
                    .HasDatabaseName("ix_site_history_site_id");

                e.HasIndex(x => x.VisitDate)
                    .HasDatabaseName("ix_site_history_visit_date");

                e.HasIndex(x => x.LegacySourceId)
                    .HasDatabaseName("ix_site_history_legacy_source_id");

                e.Property(x => x.IsDeleted)
                    .HasColumnName("is_deleted");

                e.Property(x => x.EditedAt)
                    .HasColumnName("edited_at");

                e.Property(x => x.EditedBy)
                    .HasColumnName("edited_by")
                    .HasMaxLength(100);

                e.Property(x => x.DeletedAt)
                    .HasColumnName("deleted_at");

                e.Property(x => x.DeletedBy)
                    .HasColumnName("deleted_by")
                    .HasMaxLength(100);
            });

            //CACHED SITE STUFF
            modelBuilder.Entity<CacheSiteEntity>(e =>
            {
                e.ToTable("cache_site");

                e.HasKey(x => x.CacheSiteId);

                e.Property(x => x.CacheSiteId)
                    .HasColumnName("cache_site_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SiteTypeCode)
                    .HasColumnName("site_type_code")
                    .HasMaxLength(50);

                e.Property(x => x.SiteStatus)
                    .HasColumnName("site_status")
                    .HasMaxLength(100);

                e.Property(x => x.SiteConfigName)
                    .HasColumnName("site_config_name")
                    .HasMaxLength(150);

                e.Property(x => x.PrimaryCommType)
                    .HasColumnName("primary_comm_type")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryCommType)
                    .HasColumnName("secondary_comm_type")
                    .HasMaxLength(100);

                e.Property(x => x.SiteConfigDescription)
                    .HasColumnName("site_config_description")
                    .HasMaxLength(500);

                e.Property(x => x.SourceSiteRowId)
                    .HasColumnName("source_site_row_id");

                e.Property(x => x.SourceSiteTypeId)
                    .HasColumnName("source_site_type_id");

                e.Property(x => x.SourceConfigId)
                    .HasColumnName("source_config_id");

                e.Property(x => x.SourceSiteStatusId)
                    .HasColumnName("source_site_status_id");

                e.Property(x => x.SourceSiteStatus2Id)
                    .HasColumnName("source_site_status2_id");

                e.Property(x => x.SourceAddressRowId)
                    .HasColumnName("source_address_row_id");

                e.Property(x => x.SourceGpsRowId)
                    .HasColumnName("source_gps_row_id");

                e.Property(x => x.SourceTowerApId)
                    .HasColumnName("source_tower_ap_id");

                e.Property(x => x.SourceTowerApAlt1Id)
                    .HasColumnName("source_tower_ap_alt1_id");

                e.Property(x => x.SourceTowerApAlt2Id)
                    .HasColumnName("source_tower_ap_alt2_id");

                e.Property(x => x.MonitorEnabled)
                    .HasColumnName("monitor_enabled")
                    .HasColumnType("bit(1)");

                e.Property(x => x.PreferredInterface)
                    .HasColumnName("preferred_interface")
                    .HasMaxLength(100);

                e.Property(x => x.ServiceCenter)
                    .HasColumnName("service_center")
                    .HasMaxLength(100);

                e.Property(x => x.Psec)
                    .HasColumnName("psec")
                    .HasMaxLength(100);

                e.Property(x => x.FunctionalLocationArea)
                    .HasColumnName("functional_location_area")
                    .HasMaxLength(100);

                e.Property(x => x.SiteNotes)
                    .HasColumnName("site_notes");

                e.Property(x => x.SourceAddedAt)
                    .HasColumnName("source_added_at");

                e.Property(x => x.SourceAddedBy)
                    .HasColumnName("source_added_by")
                    .HasMaxLength(100);

                e.Property(x => x.SourceModifiedAt)
                    .HasColumnName("source_modified_at");

                e.Property(x => x.SourceModifiedBy)
                    .HasColumnName("source_modified_by")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("ux_cache_site_site_id");

                e.HasIndex(x => x.SiteTypeCode)
                    .HasDatabaseName("ix_cache_site_site_type_code");

                e.HasIndex(x => x.SourceSiteRowId)
                    .HasDatabaseName("ix_cache_site_source_site_row_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("ix_cache_site_last_synced_at");
            });

            modelBuilder.Entity<CacheSiteAmsEntity>(e =>
            {
                e.ToTable("cache_site_ams");

                e.HasKey(x => x.CacheSiteAmsId);

                e.Property(x => x.CacheSiteAmsId)
                    .HasColumnName("cache_site_ams_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceAmsRowId)
                    .HasColumnName("source_ams_row_id");

                e.Property(x => x.PrimaryCommsIdentifier)
                    .HasColumnName("primary_comms_identifier")
                    .HasMaxLength(100);

                e.Property(x => x.PrimaryCommsIp)
                    .HasColumnName("primary_comms_ip")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryLanIp)
                    .HasColumnName("secondary_lan_ip")
                    .HasMaxLength(100);

                e.Property(x => x.AntennaSerialNumber)
                    .HasColumnName("antenna_serial_number")
                    .HasMaxLength(150);

                e.Property(x => x.EnclosureSerialNumber)
                    .HasColumnName("enclosure_serial_number")
                    .HasMaxLength(150);

                e.Property(x => x.EnclosureModel)
                    .HasColumnName("enclosure_model")
                    .HasMaxLength(150);

                e.Property(x => x.SourceAddedAt)
                    .HasColumnName("source_added_at");

                e.Property(x => x.SourceAddedBy)
                    .HasColumnName("source_added_by")
                    .HasMaxLength(100);

                e.Property(x => x.SourceModifiedAt)
                    .HasColumnName("source_modified_at");

                e.Property(x => x.SourceModifiedBy)
                    .HasColumnName("source_modified_by")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("ux_cache_site_ams_site_id");

                e.HasIndex(x => x.SourceAmsRowId)
                    .HasDatabaseName("ix_cache_site_ams_source_row_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("ix_cache_site_ams_last_synced_at");
            });

            modelBuilder.Entity<CacheSiteAddressEntity>(e =>
            {
                e.ToTable("cache_site_address");

                e.HasKey(x => x.CacheSiteAddressId);

                e.Property(x => x.CacheSiteAddressId)
                    .HasColumnName("cache_site_address_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceAddressRowId)
                    .HasColumnName("source_address_row_id");

                e.Property(x => x.StreetNumber)
                    .HasColumnName("street_number")
                    .HasMaxLength(50);

                e.Property(x => x.StreetName)
                    .HasColumnName("street_name")
                    .HasMaxLength(150);

                e.Property(x => x.StreetSuffix)
                    .HasColumnName("street_suffix")
                    .HasMaxLength(50);

                e.Property(x => x.StreetAddress)
                    .HasColumnName("street_address")
                    .HasMaxLength(255);

                e.Property(x => x.City)
                    .HasColumnName("city")
                    .HasMaxLength(100);

                e.Property(x => x.County)
                    .HasColumnName("county")
                    .HasMaxLength(100);

                e.Property(x => x.StateCode)
                    .HasColumnName("state_code")
                    .HasMaxLength(20);

                e.Property(x => x.ZipCode)
                    .HasColumnName("zip_code")
                    .HasMaxLength(20);

                e.Property(x => x.SourceAddedAt)
                    .HasColumnName("source_added_at");

                e.Property(x => x.SourceAddedBy)
                    .HasColumnName("source_added_by")
                    .HasMaxLength(100);

                e.Property(x => x.SourceModifiedAt)
                    .HasColumnName("source_modified_at");

                e.Property(x => x.SourceModifiedBy)
                    .HasColumnName("source_modified_by")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("ux_cache_site_address_site_id");

                e.HasIndex(x => x.SourceAddressRowId)
                    .HasDatabaseName("ix_cache_site_address_source_row_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("ix_cache_site_address_last_synced_at");
            });

            modelBuilder.Entity<CacheSiteGpsEntity>(e =>
            {
                e.ToTable("cache_site_gps");

                e.HasKey(x => x.CacheSiteGpsId);

                e.Property(x => x.CacheSiteGpsId)
                    .HasColumnName("cache_site_gps_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceGpsRowId)
                    .HasColumnName("source_gps_row_id");

                e.Property(x => x.Latitude)
                    .HasColumnName("latitude")
                    .HasPrecision(10, 7);

                e.Property(x => x.Longitude)
                    .HasColumnName("longitude")
                    .HasPrecision(10, 7);

                e.Property(x => x.Elevation)
                    .HasColumnName("elevation")
                    .HasPrecision(10, 2);

                e.Property(x => x.SourceAddedAt)
                    .HasColumnName("source_added_at");

                e.Property(x => x.SourceAddedBy)
                    .HasColumnName("source_added_by")
                    .HasMaxLength(100);

                e.Property(x => x.SourceModifiedAt)
                    .HasColumnName("source_modified_at");

                e.Property(x => x.SourceModifiedBy)
                    .HasColumnName("source_modified_by")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("ux_cache_site_gps_site_id");

                e.HasIndex(x => x.SourceGpsRowId)
                    .HasDatabaseName("ix_cache_site_gps_source_row_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("ix_cache_site_gps_last_synced_at");
            });

            modelBuilder.Entity<CacheSiteLteEntity>(e =>
            {
                e.ToTable("cache_site_lte");

                e.HasKey(x => x.CacheSiteLteId);

                e.Property(x => x.CacheSiteLteId)
                    .HasColumnName("cache_site_lte_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceLteRowId)
                    .HasColumnName("source_lte_row_id");

                e.Property(x => x.SimNumber)
                    .HasColumnName("sim_number")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryWanIp)
                    .HasColumnName("secondary_wan_ip")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryWanIp2)
                    .HasColumnName("secondary_wan_ip_2")
                    .HasMaxLength(100);

                e.Property(x => x.SourceAddedAt)
                    .HasColumnName("source_added_at");

                e.Property(x => x.SourceAddedBy)
                    .HasColumnName("source_added_by")
                    .HasMaxLength(100);

                e.Property(x => x.SourceModifiedAt)
                    .HasColumnName("source_modified_at");

                e.Property(x => x.SourceModifiedBy)
                    .HasColumnName("source_modified_by")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("ux_cache_site_lte_site_id");

                e.HasIndex(x => x.SourceLteRowId)
                    .HasDatabaseName("ix_cache_site_lte_source_row_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("ix_cache_site_lte_last_synced_at");
            });

            modelBuilder.Entity<CacheSitePmrEntity>(e =>
            {
                e.ToTable("cache_site_pmr");

                e.HasKey(x => x.CacheSitePmrId);

                e.Property(x => x.CacheSitePmrId)
                    .HasColumnName("cache_site_pmr_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourcePmrRowId)
                    .HasColumnName("source_pmr_row_id");

                e.Property(x => x.SecondaryCommsIdentifier)
                    .HasColumnName("secondary_comms_identifier")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryCommsUsername)
                    .HasColumnName("secondary_comms_username")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryCommsSsid)
                    .HasColumnName("secondary_comms_ssid")
                    .HasMaxLength(150);

                e.Property(x => x.SecondaryCommsPassword)
                    .HasColumnName("secondary_comms_password")
                    .HasMaxLength(255);

                e.Property(x => x.SourceAddedAt)
                    .HasColumnName("source_added_at");

                e.Property(x => x.SourceAddedBy)
                    .HasColumnName("source_added_by")
                    .HasMaxLength(100);

                e.Property(x => x.SourceModifiedAt)
                    .HasColumnName("source_modified_at");

                e.Property(x => x.SourceModifiedBy)
                    .HasColumnName("source_modified_by")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("ux_cache_site_pmr_site_id");

                e.HasIndex(x => x.SourcePmrRowId)
                    .HasDatabaseName("ix_cache_site_pmr_source_row_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("ix_cache_site_pmr_last_synced_at");
            });

            modelBuilder.Entity<CacheSiteTopEntity>(e =>
            {
                e.ToTable("cache_site_top");

                e.HasKey(x => x.CacheSiteTopId);

                e.Property(x => x.CacheSiteTopId)
                    .HasColumnName("cache_site_top_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceTopNameId)
                    .HasColumnName("source_top_name_id");

                e.Property(x => x.TopName)
                    .HasColumnName("top_name")
                    .HasMaxLength(150);

                e.Property(x => x.TopDescription)
                    .HasColumnName("top_description")
                    .HasMaxLength(255);

                e.Property(x => x.TopSector)
                    .HasColumnName("top_sector")
                    .HasMaxLength(100);

                e.Property(x => x.TopVip)
                    .HasColumnName("top_vip")
                    .HasMaxLength(100);

                e.Property(x => x.TopIpA)
                    .HasColumnName("top_ip_a")
                    .HasMaxLength(100);

                e.Property(x => x.TopIpB)
                    .HasColumnName("top_ip_b")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("UX_cache_site_top_site_id");

                e.HasIndex(x => x.TopName)
                    .HasDatabaseName("IX_cache_site_top_top_name");

                e.HasIndex(x => x.TopVip)
                    .HasDatabaseName("IX_cache_site_top_vip");

                e.HasIndex(x => x.TopIpA)
                    .HasDatabaseName("IX_cache_site_top_ip_a");

                e.HasIndex(x => x.TopIpB)
                    .HasDatabaseName("IX_cache_site_top_ip_b");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("IX_cache_site_top_last_synced");
            });

            modelBuilder.Entity<CacheSiteDacsEntity>(e =>
            {
                e.ToTable("cache_site_dacs");

                e.HasKey(x => x.CacheSiteDacsId);

                e.Property(x => x.CacheSiteDacsId)
                    .HasColumnName("cache_site_dacs_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceDacsRowId)
                    .HasColumnName("source_dacs_row_id");

                e.Property(x => x.PrimaryCommsIp)
                    .HasColumnName("primary_comms_ip")
                    .HasMaxLength(100);

                e.Property(x => x.TunnelIp)
                    .HasColumnName("tunnel_ip")
                    .HasMaxLength(100);

                e.Property(x => x.RtuIp)
                    .HasColumnName("rtu_ip")
                    .HasMaxLength(100);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("UX_cache_site_dacs_site_id");

                e.HasIndex(x => x.PrimaryCommsIp)
                    .HasDatabaseName("IX_cache_site_dacs_primary_ip");

                e.HasIndex(x => x.TunnelIp)
                    .HasDatabaseName("IX_cache_site_dacs_tunnel_ip");

                e.HasIndex(x => x.RtuIp)
                    .HasDatabaseName("IX_cache_site_dacs_rtu_ip");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("IX_cache_site_dacs_last_synced");
            });

            modelBuilder.Entity<CacheSiteIgsdEntity>(e =>
            {
                e.ToTable("cache_site_igsd");

                e.HasKey(x => x.CacheSiteIgsdId);

                e.Property(x => x.CacheSiteIgsdId)
                    .HasColumnName("cache_site_igsd_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceIgsdRowId)
                    .HasColumnName("source_igsd_row_id");

                e.Property(x => x.PrimaryCommsIdentifier)
                    .HasColumnName("primary_comms_identifier")
                    .HasMaxLength(150);

                e.Property(x => x.PrimaryCommsIp)
                    .HasColumnName("primary_comms_ip")
                    .HasMaxLength(100);

                e.Property(x => x.PrimaryLanIp)
                    .HasColumnName("primary_lan_ip")
                    .HasMaxLength(100);

                e.Property(x => x.PrimaryWanIp)
                    .HasColumnName("primary_wan_ip")
                    .HasMaxLength(100);

                e.Property(x => x.PrimaryTunnelIp)
                    .HasColumnName("primary_tunnel_ip")
                    .HasMaxLength(100);

                e.Property(x => x.PrimaryRtuIp)
                    .HasColumnName("primary_rtu_ip")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryCommsIdentifier)
                    .HasColumnName("secondary_comms_identifier")
                    .HasMaxLength(150);

                e.Property(x => x.SecondaryWanIp)
                    .HasColumnName("secondary_wan_ip")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryLanIp)
                    .HasColumnName("secondary_lan_ip")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryTunnelIp)
                    .HasColumnName("secondary_tunnel_ip")
                    .HasMaxLength(100);

                e.Property(x => x.SecondaryRtuIp)
                    .HasColumnName("secondary_rtu_ip")
                    .HasMaxLength(100);

                e.Property(x => x.AntennaSerialNumber)
                    .HasColumnName("antenna_serial_number")
                    .HasMaxLength(150);

                e.Property(x => x.EnclosureSerialNumber)
                    .HasColumnName("enclosure_serial_number")
                    .HasMaxLength(150);

                e.Property(x => x.EnclosureModel)
                    .HasColumnName("enclosure_model")
                    .HasMaxLength(150);

                e.Property(x => x.CyberlockSerialNumber)
                    .HasColumnName("cyberlock_serial_number")
                    .HasMaxLength(150);

                e.Property(x => x.TunnelPsk)
                    .HasColumnName("tunnel_psk")
                    .HasMaxLength(255);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("UX_cache_site_igsd_site_id");

                e.HasIndex(x => x.PrimaryCommsIp)
                    .HasDatabaseName("IX_cache_site_igsd_primary_comms_ip");

                e.HasIndex(x => x.PrimaryLanIp)
                    .HasDatabaseName("IX_cache_site_igsd_primary_lan_ip");

                e.HasIndex(x => x.PrimaryWanIp)
                    .HasDatabaseName("IX_cache_site_igsd_primary_wan_ip");

                e.HasIndex(x => x.PrimaryTunnelIp)
                    .HasDatabaseName("IX_cache_site_igsd_primary_tunnel_ip");

                e.HasIndex(x => x.PrimaryRtuIp)
                    .HasDatabaseName("IX_cache_site_igsd_primary_rtu_ip");

                e.HasIndex(x => x.SecondaryWanIp)
                    .HasDatabaseName("IX_cache_site_igsd_secondary_wan_ip");

                e.HasIndex(x => x.SecondaryLanIp)
                    .HasDatabaseName("IX_cache_site_igsd_secondary_lan_ip");

                e.HasIndex(x => x.SecondaryTunnelIp)
                    .HasDatabaseName("IX_cache_site_igsd_secondary_tunnel_ip");

                e.HasIndex(x => x.SecondaryRtuIp)
                    .HasDatabaseName("IX_cache_site_igsd_secondary_rtu_ip");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("IX_cache_site_igsd_last_synced");
            });

            modelBuilder.Entity<CacheSiteRxEntity>(e =>
            {
                e.ToTable("cache_site_rx");

                e.HasKey(x => x.CacheSiteRxId);

                e.Property(x => x.CacheSiteRxId)
                    .HasColumnName("cache_site_rx_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SiteId)
                    .HasColumnName("site_id")
                    .HasMaxLength(50)
                    .IsRequired();

                e.Property(x => x.SourceRxRowId)
                    .HasColumnName("source_rx_row_id");

                e.Property(x => x.MeterNumber)
                    .HasColumnName("meter_number")
                    .HasMaxLength(100);

                e.Property(x => x.MacAddress)
                    .HasColumnName("mac_address")
                    .HasMaxLength(100);

                e.Property(x => x.PolePoint)
                    .HasColumnName("pole_point")
                    .HasMaxLength(100);

                e.Property(x => x.TransformerGln)
                    .HasColumnName("transformer_gln")
                    .HasMaxLength(150);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.SiteId)
                    .IsUnique()
                    .HasDatabaseName("UX_cache_site_rx_site_id");

                e.HasIndex(x => x.MeterNumber)
                    .HasDatabaseName("IX_cache_site_rx_meter_number");

                e.HasIndex(x => x.MacAddress)
                    .HasDatabaseName("IX_cache_site_rx_mac_address");

                e.HasIndex(x => x.PolePoint)
                    .HasDatabaseName("IX_cache_site_rx_pole_point");

                e.HasIndex(x => x.TransformerGln)
                    .HasDatabaseName("IX_cache_site_rx_transformer_gln");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("IX_cache_site_rx_last_synced");
            });

            modelBuilder.Entity<CacheTowerEntity>(e =>
            {
                e.ToTable("cache_tower");

                e.HasKey(x => x.CacheTowerId);

                e.Property(x => x.CacheTowerId)
                    .HasColumnName("cache_tower_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.TopNameId)
                    .HasColumnName("top_name_id")
                    .IsRequired();

                e.Property(x => x.TopName)
                    .HasColumnName("top_name")
                    .HasMaxLength(150);

                e.Property(x => x.TopType)
                    .HasColumnName("top_type")
                    .HasMaxLength(100);

                e.Property(x => x.TopDescription)
                    .HasColumnName("top_description")
                    .HasMaxLength(255);

                e.Property(x => x.IpAssignment)
                    .HasColumnName("ip_assignment")
                    .HasMaxLength(100);

                e.Property(x => x.SourceGpsId)
                    .HasColumnName("source_gps_id");

                e.Property(x => x.SourceCnpAreaId)
                    .HasColumnName("source_cnp_area_id");

                e.Property(x => x.CustomerOwned)
                    .HasColumnName("customer_owned")
                    .HasColumnType("bit(1)");

                e.Property(x => x.Note)
                    .HasColumnName("note")
                    .HasColumnType("longtext");

                e.Property(x => x.Latitude)
                    .HasColumnName("latitude")
                    .HasPrecision(10, 7);

                e.Property(x => x.Longitude)
                    .HasColumnName("longitude")
                    .HasPrecision(10, 7);

                e.Property(x => x.StreetNumber)
                    .HasColumnName("street_number")
                    .HasMaxLength(50);

                e.Property(x => x.StreetName)
                    .HasColumnName("street_name")
                    .HasMaxLength(150);

                e.Property(x => x.StreetAddress)
                    .HasColumnName("street_address")
                    .HasMaxLength(255);

                e.Property(x => x.City)
                    .HasColumnName("city")
                    .HasMaxLength(100);

                e.Property(x => x.County)
                    .HasColumnName("county")
                    .HasMaxLength(100);

                e.Property(x => x.StateCode)
                    .HasColumnName("state_code")
                    .HasMaxLength(20);

                e.Property(x => x.ZipCode)
                    .HasColumnName("zip_code")
                    .HasMaxLength(20);

                e.Property(x => x.FullAddress)
                    .HasColumnName("full_address")
                    .HasMaxLength(500);

                e.Property(x => x.HistorySiteId)
                    .HasColumnName("history_site_id")
                    .HasMaxLength(50);

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => x.TopNameId)
                    .IsUnique()
                    .HasDatabaseName("UX_cache_tower_top_name_id");

                e.HasIndex(x => x.TopName)
                    .HasDatabaseName("IX_cache_tower_top_name");

                e.HasIndex(x => x.TopType)
                    .HasDatabaseName("IX_cache_tower_top_type");

                e.HasIndex(x => x.HistorySiteId)
                    .HasDatabaseName("IX_cache_tower_history_site_id");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("IX_cache_tower_last_synced");
            });

            modelBuilder.Entity<CacheTowerSectorEntity>(e =>
            {
                e.ToTable("cache_tower_sector");

                e.HasKey(x => x.CacheTowerSectorId);

                e.Property(x => x.CacheTowerSectorId)
                    .HasColumnName("cache_tower_sector_id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.TopNameId)
                    .HasColumnName("top_name_id")
                    .IsRequired();

                e.Property(x => x.TopSiteId)
                    .HasColumnName("top_site_id")
                    .IsRequired();

                e.Property(x => x.Sector)
                    .HasColumnName("sector")
                    .HasMaxLength(100);

                e.Property(x => x.TxDbm)
                    .HasColumnName("tx_dbm")
                    .HasPrecision(10, 3);

                e.Property(x => x.Downtilt)
                    .HasColumnName("downtilt")
                    .HasPrecision(10, 3);

                e.Property(x => x.ChannelNumber)
                    .HasColumnName("channel_number");

                e.Property(x => x.ChannelTxFrequency)
                    .HasColumnName("channel_tx_frequency")
                    .HasPrecision(18, 6);

                e.Property(x => x.ChannelRxFrequency)
                    .HasColumnName("channel_rx_frequency")
                    .HasPrecision(18, 6);

                e.Property(x => x.NetworkName)
                    .HasColumnName("network_name")
                    .HasMaxLength(150);

                e.Property(x => x.Vip)
                    .HasColumnName("vip")
                    .HasMaxLength(100);

                e.Property(x => x.IpA)
                    .HasColumnName("ip_a")
                    .HasMaxLength(100);

                e.Property(x => x.IpB)
                    .HasColumnName("ip_b")
                    .HasMaxLength(100);

                e.Property(x => x.Vlan)
                    .HasColumnName("vlan")
                    .HasMaxLength(100);

                e.Property(x => x.Bsid)
                    .HasColumnName("bsid")
                    .HasMaxLength(150);

                e.Property(x => x.AntennaSerialA)
                    .HasColumnName("antenna_serial_a")
                    .HasMaxLength(150);

                e.Property(x => x.AntennaSerialB)
                    .HasColumnName("antenna_serial_b")
                    .HasMaxLength(150);

                e.Property(x => x.Height)
                    .HasColumnName("height")
                    .HasPrecision(10, 2);

                e.Property(x => x.TestedHeight)
                    .HasColumnName("tested_height")
                    .HasPrecision(10, 2);

                e.Property(x => x.Bearing)
                    .HasColumnName("bearing");

                e.Property(x => x.HighMount)
                    .HasColumnName("high_mount")
                    .HasColumnType("bit(1)");

                e.Property(x => x.LastSyncedAt)
                    .HasColumnName("last_synced_at");

                e.Property(x => x.SyncRunId)
                    .HasColumnName("sync_run_id");

                e.Property(x => x.SourceRowHash)
                    .HasColumnName("source_row_hash")
                    .HasColumnType("char(64)");

                e.Property(x => x.IsActive)
                    .HasColumnName("is_active")
                    .HasColumnType("bit(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.HasIndex(x => new
                {
                    x.TopNameId,
                    x.TopSiteId
                })
                    .IsUnique()
                    .HasDatabaseName("UX_cache_tower_sector");

                e.HasIndex(x => x.TopNameId)
                    .HasDatabaseName("IX_cache_tower_sector_top_name_id");

                e.HasIndex(x => x.Sector)
                    .HasDatabaseName("IX_cache_tower_sector_sector");

                e.HasIndex(x => x.Vip)
                    .HasDatabaseName("IX_cache_tower_sector_vip");

                e.HasIndex(x => x.IpA)
                    .HasDatabaseName("IX_cache_tower_sector_ip_a");

                e.HasIndex(x => x.IpB)
                    .HasDatabaseName("IX_cache_tower_sector_ip_b");

                e.HasIndex(x => x.LastSyncedAt)
                    .HasDatabaseName("IX_cache_tower_sector_last_synced");
            });

            //END OF CACHED SITE STUFF

            // Stores technician write-up completion events for History date searches.
            // Work Orders intentionally remain sourced from the live TicketEntity record
            // so time entry always uses the latest valid maintenance/capital work order.
            modelBuilder.Entity<TicketWriteUpSubmissionEntity>(e =>
            {
                e.ToTable("ticket_writeup_submissions");

                e.HasKey(x => x.Id);

                e.Property(x => x.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.TicketId)
                    .HasColumnName("ticket_id");

                e.Property(x => x.SiteHistoryId)
                    .HasColumnName("site_history_id");

                /*
                 * The client submission ID is a nullable Guid because older submission rows
                 * predate idempotency. Pomelo stores it in the existing CHAR(36) column.
                 */
                e.Property(x => x.ClientSubmissionId)
                    .HasColumnName("client_submission_id")
                    .HasColumnType("char(36)");

                e.HasIndex(x => x.ClientSubmissionId)
                    .IsUnique()
                    .HasDatabaseName(
                        "ux_ticket_writeup_submissions_client_submission_id");

                e.Property(x => x.SubmittedByTechnicianId)
                    .HasColumnName("submitted_by_technician_id");

                e.Property(x => x.SubmittedByEmployeeId)
                    .HasColumnName("submitted_by_employee_id")
                    .HasMaxLength(100)
                    .IsRequired();

                e.Property(x => x.SubmittedByName)
                    .HasColumnName("submitted_by_name")
                    .HasMaxLength(150)
                    .IsRequired();

                e.Property(x => x.SubmittedAt)
                    .HasColumnName("submitted_at");

                e.Property(x => x.SubmittedNarrative)
                    .HasColumnName("submitted_narrative")
                    .IsRequired();

                e.Property(x => x.IsDeleted)
                    .HasColumnName("is_deleted")
                    .HasColumnType("tinyint(1)");

                e.Property(x => x.DeletedAt)
                    .HasColumnName("deleted_at");

                e.Property(x => x.DeletedBy)
                    .HasColumnName("deleted_by")
                    .HasMaxLength(100);

                e.HasIndex(x => x.SiteHistoryId)
                    .IsUnique()
                    .HasDatabaseName("ux_ticket_writeup_submissions_site_history_id");

                e.HasIndex(x => x.TicketId)
                    .HasDatabaseName("ix_ticket_writeup_submissions_ticket_id");

                e.HasIndex(x => new
                {
                    x.SubmittedByTechnicianId,
                    x.SubmittedAt
                })
                    .HasDatabaseName("ix_ticket_writeup_submissions_technician_date");

                e.HasIndex(x => new
                {
                    x.SubmittedByEmployeeId,
                    x.SubmittedAt
                })
                    .HasDatabaseName("ix_ticket_writeup_submissions_employee_date");

                e.HasOne(x => x.Ticket)
                    .WithMany()
                    .HasForeignKey(x => x.TicketId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.SubmittedByTechnician)
                    .WithMany()
                    .HasForeignKey(x => x.SubmittedByTechnicianId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.Property(x => x.EditedAt)
                    .HasColumnName("edited_at");

                e.Property(x => x.EditedBy)
                    .HasColumnName("edited_by")
                    .HasMaxLength(100);
            });

            // Maps the technicians who participated in a submitted write-up so every
            // assigned crew member receives the completed ticket in personal History.
            modelBuilder.Entity<TicketWriteUpSubmissionTechnicianEntity>(e =>
            {
                e.ToTable("ticket_writeup_submission_technicians");

                e.HasKey(x => x.Id);

                e.Property(x => x.Id)
                    .HasColumnName("id")
                    .ValueGeneratedOnAdd();

                e.Property(x => x.SubmissionId)
                    .HasColumnName("submission_id");

                e.Property(x => x.TechnicianId)
                    .HasColumnName("technician_id")
                    .HasColumnType("int unsigned");

                e.Property(x => x.EmployeeId)
                    .HasColumnName("employee_id")
                    .HasMaxLength(100)
                    .IsRequired();

                e.Property(x => x.TechnicianName)
                    .HasColumnName("technician_name")
                    .HasMaxLength(150)
                    .IsRequired();

                e.Property(x => x.IsSubmitter)
                    .HasColumnName("is_submitter")
                    .HasColumnType("tinyint(1)");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAdd();

                e.HasIndex(x => new
                {
                    x.SubmissionId,
                    x.EmployeeId
                })
                    .IsUnique()
                    .HasDatabaseName("ux_writeup_submission_technician_employee");

                e.HasIndex(x => x.SubmissionId)
                    .HasDatabaseName("ix_writeup_submission_participant_submission");

                e.HasIndex(x => new
                {
                    x.TechnicianId,
                    x.SubmissionId
                })
                    .HasDatabaseName("ix_writeup_submission_participant_technician");

                e.HasIndex(x => new
                {
                    x.EmployeeId,
                    x.SubmissionId
                })
                    .HasDatabaseName("ix_writeup_submission_participant_employee");

                /*
                 * Participant rows belong to one write-up submission. The principal-side
                 * collection is intentionally omitted because current workflows query the
                 * participant DbSet directly.
                 */
                e.HasOne(x => x.Submission)
                    .WithMany()
                    .HasForeignKey(x => x.SubmissionId)
                    .OnDelete(DeleteBehavior.Cascade);

                /*
                 * A technician may later be deleted while the stored employee ID and
                 * display name remain available on the completed-work participant row.
                 */
                e.HasOne(x => x.Technician)
                    .WithMany()
                    .HasForeignKey(x => x.TechnicianId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<DailyTicketAssignmentEntity>(e =>
            {
                e.ToTable("daily_ticket_assignments");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");

                e.Property(x => x.AssignmentDate)
                    .HasColumnName("assignment_date")
                    .HasColumnType("date");

                e.Property(x => x.TicketId)
                    .HasColumnName("ticket_id");

                e.Property(x => x.TargetType)
                    .HasColumnName("target_type")
                    .HasMaxLength(20)
                    .IsRequired();

                e.Property(x => x.TruckId)
                    .HasColumnName("truck_id");

                e.Property(x => x.TechnicianId)
                    .HasColumnName("technician_id");

                e.Property(x => x.CrewId)
                    .HasColumnName("crew_id");

                e.Property(x => x.SortOrder)
                    .HasColumnName("sort_order");

                e.Property(x => x.IsPublished)
                    .HasColumnName("is_published");

                e.Property(x => x.PublishedVersion)
                    .HasColumnName("published_version");

                e.Property(x => x.PublishedAt)
                    .HasColumnName("published_at");

                e.Property(x => x.PublishedBy)
                    .HasColumnName("published_by")
                    .HasMaxLength(100);

                e.Property(x => x.CarriedFromAssignmentId)
                    .HasColumnName("carried_from_assignment_id");

                e.Property(x => x.AssignmentNotes)
                    .HasColumnName("assignment_notes");

                e.Property(x => x.CreatedAt)
                    .HasColumnName("created_at");

                e.Property(x => x.CreatedBy)
                    .HasColumnName("created_by")
                    .HasMaxLength(100)
                    .IsRequired();

                e.Property(x => x.UpdatedAt)
                    .HasColumnName("updated_at");

                e.Property(x => x.UpdatedBy)
                    .HasColumnName("updated_by")
                    .HasMaxLength(100);

                e.HasIndex(x => new { x.AssignmentDate, x.TicketId })
                    .IsUnique()
                    .HasDatabaseName("ux_daily_assignment_date_ticket");

                e.HasIndex(x => x.AssignmentDate)
                    .HasDatabaseName("ix_daily_assignments_date");

                e.HasIndex(x => x.TicketId)
                    .HasDatabaseName("ix_daily_assignments_ticket");

                e.HasIndex(x => new { x.AssignmentDate, x.TruckId })
                    .HasDatabaseName("ix_daily_assignments_truck");

                e.HasIndex(x => new { x.AssignmentDate, x.TechnicianId })
                    .HasDatabaseName("ix_daily_assignments_technician");

                e.HasIndex(x => new { x.AssignmentDate, x.CrewId })
                    .HasDatabaseName("ix_daily_assignments_crew");

                e.HasIndex(x => new { x.AssignmentDate, x.IsPublished })
                    .HasDatabaseName("ix_daily_assignments_published");

                e.HasIndex(x => x.CarriedFromAssignmentId)
                    .HasDatabaseName("ix_daily_assignments_carried_from");

                e.HasOne(x => x.Ticket)
                    .WithMany()
                    .HasForeignKey(x => x.TicketId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.Truck)
                    .WithMany()
                    .HasForeignKey(x => x.TruckId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Technician)
                    .WithMany()
                    .HasForeignKey(x => x.TechnicianId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Crew)
                    .WithMany()
                    .HasForeignKey(x => x.CrewId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.CarriedFromAssignment)
                    .WithMany()
                    .HasForeignKey(x => x.CarriedFromAssignmentId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<DailyTicketAssignmentPublishedEntity>(e =>
            {
                e.ToTable("daily_ticket_assignment_published");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");

                e.Property(x => x.AssignmentDate)
                    .HasColumnName("assignment_date")
                    .HasColumnType("date");

                e.Property(x => x.PublishedVersion)
                    .HasColumnName("published_version");

                e.Property(x => x.TicketId)
                    .HasColumnName("ticket_id");

                e.Property(x => x.SourceAssignmentId)
                    .HasColumnName("source_assignment_id");

                e.Property(x => x.TargetType)
                    .HasColumnName("target_type")
                    .HasMaxLength(20)
                    .IsRequired();

                e.Property(x => x.TruckId)
                    .HasColumnName("truck_id");

                e.Property(x => x.TechnicianId)
                    .HasColumnName("technician_id");

                e.Property(x => x.CrewId)
                    .HasColumnName("crew_id");

                e.Property(x => x.SortOrder)
                    .HasColumnName("sort_order");

                e.Property(x => x.AssignmentNotes)
                    .HasColumnName("assignment_notes");

                e.Property(x => x.PublishedAt)
                    .HasColumnName("published_at");

                e.Property(x => x.PublishedBy)
                    .HasColumnName("published_by")
                    .HasMaxLength(100)
                    .IsRequired();

                e.HasIndex(x => new { x.AssignmentDate, x.PublishedVersion, x.TicketId })
                    .IsUnique()
                    .HasDatabaseName("ux_daily_published_date_version_ticket");

                e.HasIndex(x => new { x.AssignmentDate, x.PublishedVersion })
                    .HasDatabaseName("ix_daily_published_date_version");

                e.HasIndex(x => x.TicketId)
                    .HasDatabaseName("ix_daily_published_ticket");

                e.HasIndex(x => new { x.AssignmentDate, x.PublishedVersion, x.TruckId })
                    .HasDatabaseName("ix_daily_published_truck");

                e.HasIndex(x => new { x.AssignmentDate, x.PublishedVersion, x.TechnicianId })
                    .HasDatabaseName("ix_daily_published_technician");

                e.HasIndex(x => new { x.AssignmentDate, x.PublishedVersion, x.CrewId })
                    .HasDatabaseName("ix_daily_published_crew");

                e.HasIndex(x => x.SourceAssignmentId)
                    .HasDatabaseName("ix_daily_published_source_assignment");

                e.HasOne(x => x.Ticket)
                    .WithMany()
                    .HasForeignKey(x => x.TicketId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.SourceAssignment)
                    .WithMany()
                    .HasForeignKey(x => x.SourceAssignmentId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Truck)
                    .WithMany()
                    .HasForeignKey(x => x.TruckId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Technician)
                    .WithMany()
                    .HasForeignKey(x => x.TechnicianId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Crew)
                    .WithMany()
                    .HasForeignKey(x => x.CrewId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}