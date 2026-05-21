namespace SmartGridSuite.Api.Data.Entities
{
    public class TicketEntity
    {
        public long Id { get; set; }
        public string Site { get; set; } = "";
        public string? Notification { get; set; }
        public string Status { get; set; } = "";
        public string AssignedTech { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public string? CurrentWorkOrder { get; set; }
        public string? WorkOrderClass { get; set; } // "Maint" or "Cap"
        public string Summary { get; set; } = "";
        public string NotificationName { get; set; } = "";
        public string GroupCode { get; set; } = "";
        public int PriorityDays { get; set; }
        public string Problem { get; set; } = "";
        public string? Notes { get; set; }

        public string? DispatchNotes { get; set; }
        public string CreatedBy { get; set; } = "";
        public uint? AssignedCrewId { get; set; }

        public ulong? TaskCategoryId { get; set; }
        public string? ActionRequiredOverride { get; set; }

        public virtual CrewEntity? AssignedCrew { get; set; }
        public virtual TicketTaskCategoryEntity? TaskCategory { get; set; }
    }
}