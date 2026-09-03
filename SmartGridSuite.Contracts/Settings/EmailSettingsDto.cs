#nullable enable

namespace SmartGridSuite.Contracts.Settings
{
    public sealed class EmailSettingsDto
    {
        public bool EmailEnabled { get; set; }

        public bool DryRun { get; set; }

        public bool DailyAssignmentsEnabled { get; set; }

        public bool WriteUpsEnabled { get; set; }

        public bool BccSender { get; set; }

        public string TestRecipientOverride { get; set; } = "";

        public string AllEmailsAddress { get; set; } = "";

        public string BugFeatureRequestRecipient { get; set; } = "";
    }

    public sealed class UpdateEmailSettingsRequest
    {
        public bool EmailEnabled { get; set; }

        public bool DryRun { get; set; }

        public bool DailyAssignmentsEnabled { get; set; }

        public bool WriteUpsEnabled { get; set; }

        public bool BccSender { get; set; }

        public string TestRecipientOverride { get; set; } = "";

        public string AllEmailsAddress { get; set; } = "";

        public string BugFeatureRequestRecipient { get; set; } = "";
    }

    public sealed class SendTestEmailRequest
    {
        public string ToAddress { get; set; } = "";

        public string CreatedBy { get; set; } = "";

        public string? FromAddress { get; set; }

        public string? FromDisplayName { get; set; }
    }

    public sealed class SendTestEmailResponse
    {
        public long? LogId { get; set; }

        public string Status { get; set; } = "";

        public string Message { get; set; } = "";
    }


}