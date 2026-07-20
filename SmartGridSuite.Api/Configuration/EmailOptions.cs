namespace SmartGridSuite.Api.Configuration
{
    public sealed class EmailOptions
    {
        public const string SectionName = "Email";

        public string SmtpHost { get; set; } = "";

        public int SmtpPort { get; set; } = 25;

        public bool UseStartTls { get; set; }

        public bool UseAuthentication { get; set; }

        public string Username { get; set; } = "";

        public string Password { get; set; } = "";

        public string DefaultFromAddress { get; set; } = "";

        public string DefaultFromDisplayName { get; set; } = "Smart Grid Suite";

        public bool AllowFromOverride { get; set; } = true;
    }
}