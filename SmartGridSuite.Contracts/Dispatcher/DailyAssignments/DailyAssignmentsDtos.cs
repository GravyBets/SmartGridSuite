#nullable enable
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Dispatcher.DailyAssignments
{
    public sealed class DailyAssignmentsBoardDto
    {
        public DateTime WorkDate { get; set; }

        public int PublishedVersion { get; set; }
        public DateTime? LastPublishedAt { get; set; }
        public string? LastPublishedBy { get; set; }

        public List<DailyAssignmentTargetDto> TruckTargets { get; set; } = new();
        public List<DailyAssignmentTargetDto> TechnicianTargets { get; set; } = new();

        public List<DailyAssignmentTicketDto> TicketPool { get; set; } = new();
    }

    public sealed class DailyAssignmentTargetDto
    {
        public string TargetKey { get; set; } = "";
        public string TargetType { get; set; } = ""; // Truck or Technician

        public int? TruckId { get; set; }
        public string? TruckNumber { get; set; }
        public string? TruckStyleName { get; set; }

        public int? TechnicianId { get; set; }
        public string? TechnicianName { get; set; }
        public string? TechnicianTitle { get; set; }

        public int? CrewId { get; set; }

        public List<DailyAssignmentTechnicianDto> Technicians { get; set; } = new();
        public List<DailyAssignedTicketDto> AssignedTickets { get; set; } = new();
    }

    public sealed class DailyAssignmentTechnicianDto
    {
        public int Id { get; set; }
        public string EmployeeId { get; set; } = "";

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Name { get; set; } = "";

        public string Title { get; set; } = "";
        public string ScheduleText { get; set; } = "";

        public bool IsActive { get; set; }
        public bool IsOnShift { get; set; }

        public int? TruckId { get; set; }
        public string? TruckNumber { get; set; }
    }

    public class DailyAssignmentTicketDto
    {
        public long TicketId { get; set; }

        public string Site { get; set; } = "";
        public string NotificationName { get; set; } = "";
        public string Notification { get; set; } = "";

        public string Status { get; set; } = "";
        public bool IsClosed { get; set; }

        public bool IsFieldComplete { get; set; }

        public string AssignedTech { get; set; } = "";

        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }

        public string WorkOrder { get; set; } = "";
        public string WorkOrderClass { get; set; } = "";

        public string GroupCode { get; set; } = "";
        public int PriorityDays { get; set; }

        public string Problem { get; set; } = "";
        public string Notes { get; set; } = "";
        public string DispatchNotes { get; set; } = "";

        public ulong? TaskCategoryId { get; set; }
        public string? TaskCategoryName { get; set; }
        public string? ActionRequiredOverride { get; set; }

        public ulong? CurrentAssignmentId { get; set; }
        public string? CurrentAssignmentTargetType { get; set; }
        public int? CurrentAssignmentTruckId { get; set; }
        public string? CurrentAssignmentTruckNumber { get; set; }
        public int? CurrentAssignmentTechnicianId { get; set; }
        public string? CurrentAssignmentTechnicianName { get; set; }
        public int CurrentAssignmentSortOrder { get; set; }
        public bool CurrentAssignmentIsPublished { get; set; }
    }

    public sealed class DailyAssignedTicketDto : DailyAssignmentTicketDto
    {
        public ulong AssignmentId { get; set; }

        public string TargetType { get; set; } = "";
        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }
        public int? CrewId { get; set; }

        public int SortOrder { get; set; }

        public bool IsPublished { get; set; }
        public int PublishedVersion { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string? PublishedBy { get; set; }

        public ulong? CarriedFromAssignmentId { get; set; }

        public string AssignmentNotes { get; set; } = "";
    }

    public sealed class AssignDailyTicketsRequest
    {
        public DateTime WorkDate { get; set; }

        public List<long> TicketIds { get; set; } = new();

        // "Truck" or "Technician"
        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        // "Move" = current behavior: this becomes the ticket's only active route.
        // "Add"  = keep existing route(s) and add this target as another crew.
        public string AssignmentMode { get; set; } = "Move";

        public string? AssignmentNotes { get; set; }

        public string? UpdatedBy { get; set; }

        public bool ConfirmConflictWarnings { get; set; }
    }

    public sealed class AssignDailyTicketsResponse
    {
        public DateTime WorkDate { get; set; }

        public int AssignedCount { get; set; }

        public List<ulong> AssignmentIds { get; set; } = new();
    }

    public sealed class RemoveDailyTicketAssignmentsRequest
    {
        public DateTime WorkDate { get; set; }

        /*
         * Preferred removal identity.
         *
         * Each crew copy of a ticket has its own Daily Assignment row,
         * so AssignmentIds allow one crew's copy to be removed without
         * affecting another crew working the same ticket.
         */
        public List<ulong> AssignmentIds { get; set; } = new();

        /*
         * Legacy fallback for existing callers.
         * We will migrate the Daily Assignments UI to AssignmentIds next.
         */
        public List<long> TicketIds { get; set; } = new();

        public string? UpdatedBy { get; set; }
    }

    public sealed class RemoveDailyTicketAssignmentsResponse
    {
        public DateTime WorkDate { get; set; }

        public int RemovedCount { get; set; }

        public List<long> RemovedTicketIds { get; set; } = new();
    }

    public sealed class ReorderDailyTicketAssignmentsRequest
    {
        public DateTime WorkDate { get; set; }

        // "Truck" or "Technician"
        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        // Send the full ordered ticket list for that one target.
        public List<long> TicketIdsInOrder { get; set; } = new();

        public string? UpdatedBy { get; set; }
    }

    public sealed class ReorderDailyTicketAssignmentsResponse
    {
        public DateTime WorkDate { get; set; }

        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        public int ReorderedCount { get; set; }
    }

    public sealed class CarryOverDailyAssignmentsRequest
    {
        public DateTime WorkDate { get; set; }

        // Optional. If blank, API uses the latest published assignment date before WorkDate.
        public DateTime? FromDate { get; set; }

        public string? CreatedBy { get; set; }
    }

    public sealed class CarryOverDailyAssignmentsResponse
    {
        public DateTime WorkDate { get; set; }

        public DateTime? SourceDate { get; set; }
        public int SourcePublishedVersion { get; set; }

        public int CarriedOverCount { get; set; }
        public int SkippedAlreadyAssignedCount { get; set; }
        public int SkippedCompletedOrClosedCount { get; set; }
        public int SkippedInvalidTargetCount { get; set; }

        public List<long> CarriedOverTicketIds { get; set; } = new();
        public List<ulong> NewAssignmentIds { get; set; } = new();

        public string Message { get; set; } = "";
    }

    public sealed class PublishDailyAssignmentTargetRequest
    {
        public DateTime WorkDate { get; set; }

        // "Truck" or "Technician"
        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        public string? PublishedBy { get; set; }
    }

    public sealed class PublishDailyAssignmentTargetResponse
    {
        public DateTime WorkDate { get; set; }

        public string TargetType { get; set; } = "";

        public int? TruckId { get; set; }
        public int? TechnicianId { get; set; }

        public int PublishedVersion { get; set; }
        public DateTime PublishedAt { get; set; }
        public string PublishedBy { get; set; } = "";

        public int PublishedCount { get; set; }

        public List<long> TicketIds { get; set; } = new();

        public long? EmailLogId { get; set; }

        public string EmailStatus { get; set; } = "";

        public string EmailMessage { get; set; } = "";
    }
}