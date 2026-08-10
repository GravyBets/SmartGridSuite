using SmartGridSuite.Contracts.Settings;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class TicketWriteUpSubmissionCloseoutItemEntity
    {
        public long Id { get; set; }

        public long SubmissionId { get; set; }

        public TicketWriteUpSubmissionEntity? Submission { get; set; }

        public uint? DefinitionId { get; set; }

        public DispatchCloseoutChecklistDefinitionEntity? Definition
        {
            get;
            set;
        }

        public string DisplayNameSnapshot { get; set; } =
            string.Empty;

        public int SortOrderSnapshot { get; set; }

        public bool IsRequired { get; set; } = true;

        public string ConditionTypeSnapshot { get; set; } =
            DispatchCloseoutConditionTypes.Always;

        public uint? WriteUpFlagId { get; set; }

        public uint? ReferToOptionId { get; set; }

        public bool IsCompleted { get; set; }

        public string? CompletedBy { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}