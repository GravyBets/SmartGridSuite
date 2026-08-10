namespace SmartGridSuite.Contracts.Settings
{
    public sealed class SaveReferToOptionRequest
    {
        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
    }
}