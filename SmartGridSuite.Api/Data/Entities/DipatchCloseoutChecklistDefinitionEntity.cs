using SmartGridSuite.Contracts.Settings;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class DispatchCloseoutChecklistDefinitionEntity
    {
        public uint Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public bool IsRequired { get; set; } = true;

        public string ConditionType { get; set; } =
            DispatchCloseoutConditionTypes.Always;

        public uint? WriteUpFlagId { get; set; }

        public WriteUpFlagEntity? WriteUpFlag { get; set; }

        public uint? ReferToOptionId { get; set; }

        public ReferToOptionEntity? ReferToOption { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}