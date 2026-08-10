#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class TicketWriteUpSubmissionFlagEntity
    {
        public long SubmissionId { get; set; }

        public uint WriteUpFlagId { get; set; }

        public string DisplayNameSnapshot { get; set; } = "";

        public string SelectionSource { get; set; } = "Manual";

        public string? AutomaticReason { get; set; }

        public DateTime CreatedAt { get; set; }

        public TicketWriteUpSubmissionEntity? Submission { get; set; }

        public WriteUpFlagEntity? WriteUpFlag { get; set; }
    }
}