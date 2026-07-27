namespace SmartGridSuite.Api.Configuration
{
    public sealed class ClientVersionOptions
    {
        public const string SectionName = "ClientVersion";

        public string LatestVersion { get; set; } = "";

        public string MinimumSupportedVersion { get; set; } = "";

        public DateTimeOffset PublishedAtUtc { get; set; }

        public string InstallUrl { get; set; } = "";

        public string[] ReleaseNotes { get; set; } =
            Array.Empty<string>();
    }
}