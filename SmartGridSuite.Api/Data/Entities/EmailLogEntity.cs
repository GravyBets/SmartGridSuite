#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class EmailLogEntity
    {
        public long Id { get; set; }

        public DateTime CreatedAt { get; set; }

        public string EmailType { get; set; } = "";

        public bool EnabledAtSendTime { get; set; }

        public bool DryRun { get; set; }

        public string FromAddress { get; set; } = "";

        public string? FromDisplayName { get; set; }

        public string ToAddresses { get; set; } = "";

        public string? CcAddresses { get; set; }

        public string? BccAddresses { get; set; }

        public string Subject { get; set; } = "";

        public string? BodyPreview { get; set; }

        public string Status { get; set; } = "";

        public string? ErrorMessage { get; set; }

        public long? RelatedTicketId { get; set; }

        public string? RelatedSite { get; set; }

        public string? CreatedBy { get; set; }
    }
}