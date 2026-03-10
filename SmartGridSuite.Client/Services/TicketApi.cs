using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartGridSuite.Contracts.Tickets;

namespace SmartGridSuite.Client.Services
{
    public sealed class TicketsApi
    {
        private readonly ApiClient _api;

        public TicketsApi(ApiClient api) => _api = api;

        public async Task<List<TicketListItemDto>> GetTicketsAsync(
            string? status = null,
            string? tech = null,
            DateTime? from = null,
            DateTime? to = null,
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

        public async Task<long> CreateTicketAsync(CreateTicketRequest req, CancellationToken ct = default)
        {
            var res = await _api.PostAsync<CreateTicketRequest, CreateTicketResponse>("api/tickets", req, ct);
            return res?.Id ?? 0;
        }
    }
}