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

        [Column("show_in_filter")]
        public bool ShowInFilter { get; set; }

        [Column("send_to_dispatch_tasks")]
        public bool SendToDispatchTasks { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}