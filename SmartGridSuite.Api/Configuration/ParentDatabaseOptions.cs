namespace SmartGridSuite.Api.Configuration
{
    public sealed class ParentDatabaseOptions
    {
        public const string SectionName = "ParentDatabase";
        public string ConnectionString { get; set; } = "";
    }
}
