namespace SmartGridSuite.Contracts.Settings
{
    public sealed class DispatchCloseoutChecklistDefinitionDto
    {
        public uint Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public int SortOrder { get; set; }

        public bool IsRequired { get; set; }

        public string ConditionType { get; set; } =
            DispatchCloseoutConditionTypes.Always;

        public uint? WriteUpFlagId { get; set; }

        public string? WriteUpFlagName { get; set; }

        public uint? ReferToOptionId { get; set; }

        public string? ReferToOptionName { get; set; }

        public string TriggerName =>
            !string.IsNullOrWhiteSpace(WriteUpFlagName)
                ? WriteUpFlagName
                : ReferToOptionName ?? string.Empty;
    }
}