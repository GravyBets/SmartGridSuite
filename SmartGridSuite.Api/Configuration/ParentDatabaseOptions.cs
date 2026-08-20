namespace SmartGridSuite.Api.Configuration
{
    public sealed class ParentDatabaseOptions
    {
        public const string SectionName = "ParentDatabase";

        public string ConnectionString { get; set; } = "";

        /*
         * When enabled, the Linux API supplies an explicit Windows/AD
         * identity to SqlClient's SSPI authentication provider.
         *
         * The password should come from the server environment file,
         * never appsettings.json or Git.
         */
        public bool UseExplicitWindowsCredentials { get; set; }

        public string WindowsDomain { get; set; } = "";

        public string WindowsUsername { get; set; } = "";

        public string WindowsPassword { get; set; } = "";
    }
}