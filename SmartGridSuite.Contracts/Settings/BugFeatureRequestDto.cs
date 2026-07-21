#nullable enable

namespace SmartGridSuite.Contracts.Settings
{
    public sealed class SubmitBugFeatureRequest
    {
        public string RequestType { get; set; } = "";

        public string ApplicationArea { get; set; } = "";

        public string Title { get; set; } = "";

        public string Details { get; set; } = "";

        public string SubmittedBy { get; set; } = "";

        public string ApplicationVersion { get; set; } = "";
    }

    public sealed class SubmitBugFeatureResponse
    {
        public bool Sent { get; set; }

        public long? LogId { get; set; }

        public string Status { get; set; } = "";

        public string Message { get; set; } = "";
    }
}