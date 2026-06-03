using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartGridSuite.Api.Data.Entities
{
    [Table("ticket_statuses")]
    public class TicketStatusEntity
    {
        [Key]
        [Column("id")]
        public ulong Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("is_closed")]
        public bool IsClosed { get; set; }

        [Column("is_field_complete")]
        public bool IsFieldComplete { get; set; }

        [Column("show_in_filter")]
        public bool ShowInFilter { get; set; }

        [Column("send_to_dispatch_tasks")]
        public bool SendToDispatchTasks { get; set; }

        [Column("include_in_summary")]
        public bool IncludeInSummary { get; set; } = true;

        [Column("is_writeup_submit_target")]
        public bool IsWriteUpSubmitTarget { get; set; }

        [Column("is_assignment_publish_target")]
        public bool IsAssignmentPublishTarget { get; set; }

        [Column("is_unassignment_target")]
        public bool IsUnassignmentTarget { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}