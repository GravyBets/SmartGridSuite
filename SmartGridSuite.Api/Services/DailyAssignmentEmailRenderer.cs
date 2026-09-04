#nullable enable

using SmartGridSuite.Api.Data.Entities;
using System.Net;
using System.Text;

namespace SmartGridSuite.Api.Services
{
    public static class DailyAssignmentEmailRenderer
    {
        public static string BuildDailyAssignmentPublishedEmailBody(
            DateTime workDate,
            string targetDisplay,
            string truckNumberDisplay,
            string publishedBy,
            DateTime publishedAt,
            string emailTitle,
            string changeSummaryHtml,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> publishedRows,
            IReadOnlyDictionary<long, TicketEntity> ticketsById)
        {
            static string H(string? value)
                => WebUtility.HtmlEncode((value ?? string.Empty).Trim());

            static string DashIfBlank(string? value)
            {
                var clean = (value ?? string.Empty).Trim();

                return string.IsNullOrWhiteSpace(clean)
                    ? "—"
                    : WebUtility.HtmlEncode(clean);
            }
            var truckRowHtml = string.IsNullOrWhiteSpace(truckNumberDisplay)
                ? ""
                : $$"""
                        <tr>
                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Truck</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(truckNumberDisplay)}}</td>
                        <td></td>
                        <td></td>
                        </tr>
                    """;

            var sb = new StringBuilder();

            sb.AppendLine($$"""
                <!DOCTYPE html>
                <html>
                <body style="margin:0; padding:0; background:#f3f4f6; font-family:Segoe UI, Arial, sans-serif; color:#111827;">
                  <div style="max-width:1100px; margin:0 auto; padding:24px;">
                    <div style="background:#ffffff; border:1px solid #d1d5db; border-radius:12px; overflow:hidden;">
                      <div style="background:#1f2937; color:#ffffff; padding:18px 22px;">
                        <div style="font-size:22px; font-weight:700;">{{H(emailTitle)}}</div>
                      </div>
                """);

            sb.AppendLine($$"""
                <div style="padding:18px 22px;">
                <table cellpadding="0" cellspacing="0" style="width:100%; margin-bottom:18px; border-collapse:collapse;">
                    <tr>
                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Date</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{workDate:MM/dd/yyyy}}</td>

                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Assigned To</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 0;">{{H(targetDisplay)}}</td>
                    </tr>
                    <tr>
                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Published By</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(publishedBy)}}</td>

                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Published At</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 0;">{{publishedAt:MM/dd/yyyy HH:mm}}</td>
                    </tr>
                    {{truckRowHtml}}
                </table>

                {{changeSummaryHtml}}

                <div style="font-size:15px; font-weight:700; margin:0 0 8px 0;">Current Route</div>

                <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db;">
                  <thead>
                    <tr style="background:#e5e7eb;">
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">#</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Site</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification Name</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Problem</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Work Order</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">WO Type</th>
                    </tr>
                  </thead>
                  <tbody>
                """);

            var rowNumber = 0;

            foreach (var assignment in publishedRows
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.Id))
            {
                if (!ticketsById.TryGetValue(assignment.TicketId, out var ticket))
                    continue;

                rowNumber++;

                var background = rowNumber % 2 == 0
                    ? "#f9fafb"
                    : "#ffffff";

                sb.AppendLine($$"""
                    <tr style="background:{{background}};">
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:600;">{{rowNumber}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:700;">{{DashIfBlank(ticket.Site)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.NotificationName)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.Problem)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.Notification)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.CurrentWorkOrder)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(NormalizeWorkOrderType(ticket.WorkOrderClass))}}</td>
                    </tr>
                    """);

                var assignmentNotes = (assignment.AssignmentNotes ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(assignmentNotes))
                {
                    sb.AppendLine($$"""
                        <tr style="background:{{background}};">
                            <td style="font-size:12px; padding:8px 10px; border:1px solid #d1d5db;"></td>
                            <td colspan="6" style="font-size:12px; padding:8px 10px; border:1px solid #d1d5db; color:#374151;">
                            <strong>Assignment Notes:</strong> {{H(assignmentNotes)}}
                            </td>
                        </tr>
                        """);
                }
            }

            if (rowNumber == 0)
            {
                sb.AppendLine("""
                    <tr>
                        <td colspan="7" style="font-size:13px; padding:14px 10px; border:1px solid #d1d5db; color:#6b7280; font-style:italic;">
                        No ticket details were available.
                        </td>
                    </tr>
                    """);
            }

            sb.AppendLine("""
                          </tbody>
                        </table>

                        <div style="font-size:12px; color:#6b7280; margin-top:16px;">
                          This message was generated by SmartGridSuite.
                        </div>
                      </div>
                    </div>
                  </div>
                </body>
                </html>
                """);

            return sb.ToString();
        }

        public static string BuildDailyAssignmentChangeSummaryHtml(
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> previousRows,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> currentRows,
            IReadOnlyDictionary<long, TicketEntity> ticketsById)
        {
            static string H(string? value)
                => WebUtility.HtmlEncode((value ?? string.Empty).Trim());

            static string TicketLabel(
                long ticketId,
                IReadOnlyDictionary<long, TicketEntity> ticketsById)
            {
                if (!ticketsById.TryGetValue(ticketId, out var ticket))
                    return $"Ticket {ticketId}";

                var site = (ticket.Site ?? string.Empty).Trim();
                var notificationName = (ticket.NotificationName ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(site) &&
                    !string.IsNullOrWhiteSpace(notificationName))
                {
                    return $"{site} - {notificationName}";
                }

                if (!string.IsNullOrWhiteSpace(site))
                    return site;

                if (!string.IsNullOrWhiteSpace(notificationName))
                    return notificationName;

                return $"Ticket {ticketId}";
            }

            static string FormatTicketList(
                IEnumerable<long> ticketIds,
                IReadOnlyDictionary<long, TicketEntity> ticketsById)
            {
                var labels = ticketIds
                    .Select(id => H(TicketLabel(id, ticketsById)))
                    .ToList();

                return labels.Count == 0
                    ? "—"
                    : string.Join("<br/>", labels);
            }

            var previousOrdered = previousRows
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .GroupBy(x => x.TicketId)
                .Select(g => g.First())
                .ToList();

            var currentOrdered = currentRows
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .GroupBy(x => x.TicketId)
                .Select(g => g.First())
                .ToList();

            var previousTicketIds = previousOrdered
                .Select(x => x.TicketId)
                .ToList();

            var currentTicketIds = currentOrdered
                .Select(x => x.TicketId)
                .ToList();

            var previousTicketIdSet = previousTicketIds.ToHashSet();
            var currentTicketIdSet = currentTicketIds.ToHashSet();

            var addedTicketIds = currentTicketIds
                .Where(id => !previousTicketIdSet.Contains(id))
                .ToList();

            var removedTicketIds = previousTicketIds
                .Where(id => !currentTicketIdSet.Contains(id))
                .ToList();

            var previousIndexByTicketId = previousTicketIds
                .Select((id, index) => new
                {
                    TicketId = id,
                    RouteOrder = index + 1
                })
                .ToDictionary(x => x.TicketId, x => x.RouteOrder);

            var currentIndexByTicketId = currentTicketIds
                .Select((id, index) => new
                {
                    TicketId = id,
                    RouteOrder = index + 1
                })
                .ToDictionary(x => x.TicketId, x => x.RouteOrder);

            var reorderedTicketIds = currentTicketIds
                .Where(id =>
                    previousIndexByTicketId.ContainsKey(id) &&
                    currentIndexByTicketId.ContainsKey(id) &&
                    previousIndexByTicketId[id] != currentIndexByTicketId[id])
                .ToList();

            var rows = new List<(string Change, string Details)>();

            if (addedTicketIds.Count > 0)
            {
                rows.Add((
                    $"Added ({addedTicketIds.Count})",
                    FormatTicketList(addedTicketIds, ticketsById)));
            }

            if (removedTicketIds.Count > 0)
            {
                rows.Add((
                    $"Removed ({removedTicketIds.Count})",
                    FormatTicketList(removedTicketIds, ticketsById)));
            }

            if (reorderedTicketIds.Count > 0)
            {
                var reorderedDetails = reorderedTicketIds
                    .Select(id =>
                        $"{H(TicketLabel(id, ticketsById))}: " +
                        $"#{previousIndexByTicketId[id]} → #{currentIndexByTicketId[id]}")
                    .ToList();

                rows.Add((
                    $"Route Order Changed ({reorderedTicketIds.Count})",
                    string.Join("<br/>", reorderedDetails)));
            }

            if (rows.Count == 0)
            {
                rows.Add((
                    "Republished",
                    "No ticket additions, removals, or route-order changes were detected."));
            }

            var sb = new StringBuilder();

            sb.AppendLine("""
                <div style="margin-bottom:18px;">
                  <div style="font-size:15px; font-weight:700; margin:0 0 8px 0;">Changes Since Previous Publish</div>

                  <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db;">
                    <thead>
                      <tr style="background:#e5e7eb;">
                        <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db; width:220px;">Change</th>
                        <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Details</th>
                      </tr>
                    </thead>
                    <tbody>
                """);

            foreach (var row in rows)
            {
                sb.AppendLine($$"""
                      <tr>
                        <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:700;">{{H(row.Change)}}</td>
                        <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{row.Details}}</td>
                      </tr>
                    """);
            }

            sb.AppendLine("""
                    </tbody>
                  </table>
                </div>
                """);

            return sb.ToString();
        }

        private static string NormalizeWorkOrderType(
            string? workOrderClass)
        {
            var value =
                (workOrderClass ?? string.Empty).Trim();

            if (value.Equals(
                    "Cap",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Equals(
                    "Capital",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Capital";
            }

            if (value.Equals(
                    "Maint",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Equals(
                    "Maintenance",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Maintenance";
            }

            if (value.Equals(
                    "Dist",
                    StringComparison.OrdinalIgnoreCase) ||
                value.Equals(
                    "Distribution",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Distribution";
            }

            return "";
        }
    }
}