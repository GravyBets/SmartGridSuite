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
        string ImportStatus,   // Ready / Already Exists / Invalid
        string Message
    );

    public sealed record SapQueueImportCommitRow(
        int RowNumber,
        string Notification,
        string? WorkOrder,
        DateTime NotificationDate,
        string Description,
        string ParsedSite
    );

    public sealed record SapQueueImportCommitRequest(
        string CreatedBy,
        List<SapQueueImportCommitRow> Rows
    );

    public sealed record SapQueueImportCommitResultRow(
        int RowNumber,
        string Notification,
        string ImportStatus,   // Imported / Already Exists / Invalid
        string Message,
        long? TicketId
    );

    public sealed record SapQueueImportCommitResponse(
        int ImportedCount,
        int AlreadyExistsCount,
        int InvalidCount,
        List<SapQueueImportCommitResultRow> Rows
    );
}