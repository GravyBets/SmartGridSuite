#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class SiteHistoryEntity
    {
        public long HistoryId { get; set; }

        public int? LegacySourceId { get; set; }

        public string SourceType { get; set; } = "";

        public string? SourceFile { get; set; }

        public string SiteId { get; set; } = "";

        public DateTime? VisitDate { get; set; }

        public string? PrimaryTech { get; set; }

        public string? SecondaryTech { get; set; }

        public string? Narrative { get; set; }

        public string? IssueText { get; set; }

        public DateTime ImportedAt { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? EditedAt { get; set; }

        public string? EditedBy { get; set; }

        public DateTime? DeletedAt { get; set; }

        public string? DeletedBy { get; set; }
    }
}