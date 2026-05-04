namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class AppSettingEntity
    {
        public string SettingKey { get; set; } = "";
        public string? SettingValue { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}