namespace SmartGridSuite.Contracts.Settings
{
    public sealed class SaveWriteUpFlagRequest
    {
        public string DisplayName { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public bool IsTechnicianVisible { get; set; } = true;
    }
}