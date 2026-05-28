using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartGridSuite.Contracts.Tickets;
using SmartGridSuite.Contracts.Dispatcher;
using System.Security;


namespace SmartGridSuite.Client.Services
{
    public sealed class TicketsApi
    {
        private readonly ApiClient _api;

        public TicketsApi(ApiClient api) => _api = api;

        public async Task<List<TicketListItemDto>> GetTicketsAsync(string? status = null, string? tech = null, DateTime? from = null, DateTime? to = null,
            CancellationToken ct = default)
        {
            var qs = new List<string>();

            if (!string.IsNullOrWhiteSpace(status))
                qs.Add($"status={Uri.EscapeDataString(status)}");

            if (!string.IsNullOrWhiteSpace(tech))
                qs.Add($"tech={Uri.EscapeDataString(tech)}");

            if (from.HasValue)
                qs.Add($"from={from.Value:yyyy-MM-dd}");

            if (to.HasValue)
                qs.Add($"to={to.Value:yyyy-MM-dd}");

            var path = "api/tickets" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
            return await _api.GetAsync<List<TicketListItemDto>>(path, ct) ?? new();
        }

        public async Task<TicketQueryResponse> QueryTicketsAsync(TicketQueryRequest req, CancellationToken ct = default)
        {
            return await _api.PostAsync<TicketQueryRequest, TicketQueryResponse>(
                       "api/tickets/query",
                       req,
                       ct)
                   ?? new TicketQueryResponse();
        }

        public async Task<List<TicketListItemDto>> GetTicketsBySiteAsync(string siteId, CancellationToken ct = default)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return new();

            return await _api.GetAsync<List<TicketListItemDto>>(
                       $"api/tickets/by-site/{Uri.EscapeDataString(siteId)}",
                       ct)
                   ?? new();
        }

        public async Task<long> CreateTicketAsync(CreateTicketRequest req, CancellationToken ct = default)
        {
            var res = await _api.PostAsync<CreateTicketRequest, CreateTicketResponse>("api/tickets", req, ct);
            return res?.Id ?? 0;
        }

        public async Task<long> UpdateTicketAsync(long id, UpdateTicketRequest req, CancellationToken ct = default)
        {
            var res = await _api.PostAsync<UpdateTicketRequest, UpdateTicketResponse>(
                $"api/tickets/{id}/update",
                req,
                ct);

            return res?.Id ?? 0;
        }

        public async Task<List<SapQueueImportPreviewResultRow>> PreviewSapQueueImportAsync(SapQueueImportPreviewRequest req, CancellationToken ct = default)
        {
            return await _api.PostAsync<SapQueueImportPreviewRequest, List<SapQueueImportPreviewResultRow>>(
                       "api/tickets/sap-import/preview", req, ct)
                   ?? new();
        }

        public async Task<SapQueueImportCommitResponse> CommitSapQueueImportAsync(SapQueueImportCommitRequest req, CancellationToken ct = default)
        {
            return await _api.PostAsync<SapQueueImportCommitRequest, SapQueueImportCommitResponse>(
                       "api/tickets/sap-import/commit", req, ct)
                   ?? new SapQueueImportCommitResponse(0, 0, 0, new());
        }

        public async Task<List<DispatchTaskListItemDto>> GetDispatchTasksAsync(CancellationToken ct = default)
        {
            return await _api.GetAsync<List<DispatchTaskListItemDto>>("api/tickets/dispatch-tasks", ct) ?? new();
        }

        public async Task<long> ResolveDispatchTaskAsync(long ticketId, CancellationToken ct = default)
        {
            var res = await _api.PostAsync<object, UpdateTicketResponse>(
                $"api/tickets/{ticketId}/resolve-dispatch-task",
                new { },
                ct);

            return res?.Id ?? 0;
        }

        // Request Capital
        public async Task RequestCapitalAsync(long id, string reason, string requestedBy = "Unknown", CancellationToken ct = default)
        {
            await _api.PostAsync<TicketActionReasonRequest, UpdateTicketResponse>(
                $"api/tickets/{id}/request-capital",
                new TicketActionReasonRequest
                {
                    Reason = reason ?? string.Empty,
                    RequestedBy = requestedBy ?? "Unknown"
                },
                ct);
        }

        // Request Maintenance
        public async Task RequestMaintenanceAsync(long id, string reason, string requestedBy = "Unknown", CancellationToken ct = default)
        {
            await _api.PostAsync<TicketActionReasonRequest, UpdateTicketResponse>(
                $"api/tickets/{id}/request-maintenance",
                new TicketActionReasonRequest
                {
                    Reason = reason ?? string.Empty,
                    RequestedBy = requestedBy ?? "Unknown"
                },
                ct);
        }

        // Request Ticket
        public async Task<long> RequestTicketAsync(string site, string reason, string requestedBy = "Unknown", CancellationToken ct = default)
        {
            var cleanSite = (site ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanSite))
                return 0;

            var existingTickets = await GetTicketsBySiteAsync(cleanSite, ct);

            var existingRequest = existingTickets
                .Where(x =>
                    string.Equals(x.NotificationName, "Ticket requested from Site Dashboard", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Status, "Closed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Status, "Canceled", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastActivityAt)
                .FirstOrDefault();

            if (existingRequest is not null)
                return existingRequest.Id;

            var cleanReason = string.IsNullOrWhiteSpace(reason)
                ? "Ticket requested from Site Dashboard."
                : reason.Trim();

            var request = new CreateTicketRequest(
                Site: cleanSite,
                NotificationName: "Ticket requested from Site Dashboard",
                Notification: string.Empty,
                WorkOrder: null,
                WorkOrderClass: string.Empty,
                GroupCode: string.Empty,
                PriorityDays: 0,
                Problem: cleanReason,
                TaskCategoryId: null,
                ActionRequiredOverride: "Review ticket request from Site Dashboard",
                AssignedTech: "(Unassigned)",
                Status: "Needs Review",
                Notes: $"Ticket requested from Site Dashboard.{Environment.NewLine}Reason: {cleanReason}",
                CreatedBy: requestedBy ?? "Unknown"
            );

            return await CreateTicketAsync(request, ct);
        }

        //Add Write-Up to Ticket
        public async Task SubmitWriteUpAsync(long ticketId, string finalWriteUpText, string siteHistoryWriteUpText, string submittedBy = "Unknown",
            CancellationToken ct = default)
        {
            await _api.PostAsync<SubmitTicketWriteUpRequest, UpdateTicketResponse>(
                $"api/tickets/{ticketId}/submit-writeup",
                new SubmitTicketWriteUpRequest
                {
                    FinalWriteUpText = finalWriteUpText ?? string.Empty,
                    SiteHistoryWriteUpText = siteHistoryWriteUpText ?? string.Empty,
                    SubmittedBy = submittedBy ?? "Unknown"
                },
                ct);
        }
    }
}