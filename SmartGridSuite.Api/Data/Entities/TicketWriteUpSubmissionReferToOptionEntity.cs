#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class TicketWriteUpSubmissionReferToOptionEntity
    {
        public long SubmissionId { get; set; }

        public uint ReferToOptionId { get; set; }

        public string DisplayNameSnapshot { get; set; } = "";

        public DateTime CreatedAt { get; set; }

        public TicketWriteUpSubmissionEntity? Submission { get; set; }

        public ReferToOptionEntity? ReferToOption { get; set; }
    }
}