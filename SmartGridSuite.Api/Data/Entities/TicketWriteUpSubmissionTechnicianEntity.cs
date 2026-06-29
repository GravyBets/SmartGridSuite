#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public class TicketWriteUpSubmissionTechnicianEntity
    {
        public long Id { get; set; }

        public long SubmissionId { get; set; }

        public uint? TechnicianId { get; set; }

        public string EmployeeId { get; set; } = "";

        public string TechnicianName { get; set; } = "";

        public bool IsSubmitter { get; set; }

        public DateTime CreatedAt { get; set; }

        public virtual TicketWriteUpSubmissionEntity? Submission { get; set; }

        public virtual TechnicianEntity? Technician { get; set; }
    }
}