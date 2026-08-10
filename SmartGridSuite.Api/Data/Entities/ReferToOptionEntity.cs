namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class ReferToOptionEntity
    {
        public uint Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}