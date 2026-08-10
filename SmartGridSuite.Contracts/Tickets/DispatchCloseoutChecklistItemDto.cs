namespace SmartGridSuite.Contracts.Tickets
{
    public sealed class DispatchCloseoutChecklistItemDto
    {
        public long Id { get; set; }

        public long SubmissionId { get; set; }

        public uint? DefinitionId { get; set; }

        public string DisplayName { get; set; } = "";

        public int SortOrder { get; set; }

        public bool IsRequired { get; set; }

        public string ConditionType { get; set; } = "";

        public uint? WriteUpFlagId { get; set; }

        public uint? ReferToOptionId { get; set; }

        public bool IsCompleted { get; set; }

        public string CompletedBy { get; set; } = "";

        public DateTime? CompletedAt { get; set; }
    }

    public sealed class UpdateDispatchCloseoutChecklistItemRequest
    {
        public bool IsCompleted { get; set; }

        public string UpdatedBy { get; set; } = "";
    }
}