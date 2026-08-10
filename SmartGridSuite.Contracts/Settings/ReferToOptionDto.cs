namespace SmartGridSuite.Contracts.Settings
{
    public sealed class ReferToOptionDto
    {
        public uint Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public int SortOrder { get; set; }
    }
}