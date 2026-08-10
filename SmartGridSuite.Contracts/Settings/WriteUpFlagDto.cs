namespace SmartGridSuite.Contracts.Settings
{
    public sealed class WriteUpFlagDto
    {
        public uint Id { get; set; }

        public string DisplayName { get; set; } = "";

        public bool IsActive { get; set; }

        public int SortOrder { get; set; }

        public bool IsTechnicianVisible { get; set; }

        public bool IsSystem { get; set; }

        public string SystemKey { get; set; } = "";
    }
}