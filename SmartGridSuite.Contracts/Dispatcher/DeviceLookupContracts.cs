using System;
using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Dispatcher
{
    public enum DeviceLookupSearchType
    {
        DeviceSerialNumber,
        Site,
        IpAddress,
        Sim,
        Notification,
        WorkOrder,
        HistoryText
    }

    public sealed class DeviceLookupResponseDto
    {
        public string Query { get; set; } = string.Empty;

        public List<DeviceLookupRecordDto> ParentRecords { get; set; } = new();

        public List<string> RelatedSiteIds { get; set; } = new();

        public List<DeviceLookupTicketDto> Tickets { get; set; } = new();

        public List<DeviceLookupHistoryDto> SiteHistory { get; set; } = new();

        public List<DeviceLookupSiteNoteDto> SiteNotes { get; set; } = new();

        public string Warning { get; set; } = string.Empty;
    }

    public sealed class DeviceLookupRecordDto
    {
        public string Source { get; set; } = string.Empty;

        public string RecordType { get; set; } = string.Empty;

        public string SiteId { get; set; } = string.Empty;

        public string MatchField { get; set; } = string.Empty;

        public List<DeviceLookupFieldDto> Fields { get; set; } = new();
    }

    public sealed class DeviceLookupFieldDto
    {
        public string Label { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }

    public sealed class DeviceLookupTicketDto
    {
        public long TicketId { get; set; }

        public string Site { get; set; } = string.Empty;

        public string Notification { get; set; } = string.Empty;

        public string WorkOrder { get; set; } = string.Empty;

        public string WorkOrderClass { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public string Problem { get; set; } = string.Empty;

        public string DispatchNotes { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime LastActivityAt { get; set; }
    }

    public sealed class DeviceLookupSiteNoteDto
    {
        public ulong Id { get; set; }

        public string SiteId { get; set; } = string.Empty;

        public string NoteType { get; set; } = string.Empty;

        public string NoteText { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public string UpdatedBy { get; set; } = string.Empty;

        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class DeviceLookupHistoryDto
    {
        public long HistoryId { get; set; }

        public string SiteId { get; set; } = string.Empty;

        public DateTime? VisitDate { get; set; }

        public string PrimaryTech { get; set; } = string.Empty;

        public string SecondaryTech { get; set; } = string.Empty;

        public string IssueText { get; set; } = string.Empty;

        public string Narrative { get; set; } = string.Empty;

        public string SourceType { get; set; } = string.Empty;
    }
}
