namespace SmartGridSuite.Contracts.Settings
{
    public sealed class SaveDispatchCloseoutChecklistDefinitionRequest
    {
        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public bool IsRequired { get; set; } = true;

        public string ConditionType { get; set; } =
            DispatchCloseoutConditionTypes.Always;

        public uint? WriteUpFlagId { get; set; }

        public uint? ReferToOptionId { get; set; }
    }
}