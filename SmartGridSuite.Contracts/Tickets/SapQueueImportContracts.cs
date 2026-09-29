using System;
using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Tickets
{
    public sealed record SapQueueImportPreviewRow(
        int RowNumber,
        string Notification,
        string? WorkOrder,
        DateTime? NotificationDate,
        string Description
    );

    public sealed record SapQueueImportPreviewRequest(
        List<SapQueueImportPreviewRow> Rows
    );

    public sealed record SapQueueImportPreviewResultRow(
        int RowNumber,
        string Notification,
        string? WorkOrder,
        DateTime? NotificationDate,
        string Description,
        string ParsedSite,
        string ImportStatus,
        string Message,

        // Spreadsheet = row came from the loaded SAP workbook.
        // Existing App = active SmartGridSuite ticket surfaced
        // during reconciliation.
        string RowSource = "Spreadsheet",

        // Populated when this preview row represents an
        // already-existing SmartGridSuite ticket.
        long? ExistingTicketId = null,

        // Current SmartGridSuite status for an existing ticket.
        string CurrentTicketStatus = "",

        // True when the dispatcher needs to pay attention to
        // the row because of a reconciliation condition.
        bool RequiresReview = false,

        // More specific explanation of why the row requires review.
        string ReviewReason = "",

        // Existing SmartGridSuite WO when a spreadsheet notification
        // already exists in the application.
        string ExistingWorkOrder = "",

        // True when SAP has a different nonblank WO for an active
        // SmartGridSuite ticket and commit should synchronize it.
        bool WorkOrderChanged = false
    );

    public sealed record SapQueueImportCommitRow(
        int RowNumber,
        string Notification,
        string? WorkOrder,
        DateTime NotificationDate,
        string Description,
        string ParsedSite,

        // Normally Open when the site was parsed successfully.
        // Conflict rows can deliberately be imported into another
        // configured ticket status such as Needs Review.
        string? TargetStatus = null
    );

    public sealed record SapQueueExistingTicketAction(
        long TicketId,

        // Keep = leave the ticket completely unchanged.
        // ChangeStatus = set TargetStatus.
        string Action,

        string? TargetStatus
    );

    public sealed record SapQueueWorkOrderUpdate(
        int RowNumber,
        long TicketId,
        string Notification,
        string NewWorkOrder
    );

    public sealed record SapQueueImportCommitRequest(
        string CreatedBy,
        List<SapQueueImportCommitRow> Rows,

        // Existing SmartGridSuite tickets surfaced during reconciliation.
        // Optional so older callers remain compatible.
        List<SapQueueExistingTicketAction>? ExistingTicketActions = null,

        // Existing notification rows where SAP now reports a different
        // nonblank Work Order. These are synchronized, not re-imported.
        List<SapQueueWorkOrderUpdate>? WorkOrderUpdates = null
    );

    public sealed record SapQueueImportCommitResultRow(
        int RowNumber,
        string Notification,
        string ImportStatus,
        string Message,
        long? TicketId,

        string RowSource = "Spreadsheet"
    );

    public sealed record SapQueueImportCommitResponse(
        int ImportedCount,
        int AlreadyExistsCount,
        int InvalidCount,
        List<SapQueueImportCommitResultRow> Rows,

        int ExistingKeptCount = 0,
        int ExistingStatusChangedCount = 0,
        int ExistingWorkOrderUpdatedCount = 0
    );
}