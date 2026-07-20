using SmartGridSuite.Contracts.Administration;
using SmartGridSuite.Contracts.Administration.Ticket;
using SmartGridSuite.Contracts.Administration.Ticket.Status;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace SmartGridSuite.Client.Services
{
    public sealed class TicketAdminApi
    {
        private readonly ApiClient _api;

        public TicketAdminApi(ApiClient api)
        {
            _api = api;
        }

        public async Task<List<TicketStatusDto>> GetStatusesAsync(CancellationToken ct = default)
        {
            return await _api.GetAsync<List<TicketStatusDto>>("api/admin/tickets/statuses", ct) ?? new();
        }

        public async Task<List<TicketTaskCategoryDto>> GetTaskCategoriesAsync(CancellationToken ct = default)
        {
            return await _api.GetAsync<List<TicketTaskCategoryDto>>("api/admin/tickets/task-categories", ct) ?? new();
        }

        public async Task<TicketStatusDto> CreateStatusAsync(CreateTicketStatusRequest request, CancellationToken ct = default)
        {
            return await _api.PostAsync<CreateTicketStatusRequest, TicketStatusDto>(
                       "api/admin/tickets/statuses", request, ct)
                   ?? new TicketStatusDto();
        }

        public async Task UpdateStatusAsync(UpdateTicketStatusRequest request, CancellationToken ct = default)
        {
            await _api.PutAsync<UpdateTicketStatusRequest>(
                $"api/admin/tickets/statuses/{request.Id}",
                request,
                ct);
        }

        public async Task DeactivateStatusAsync(ulong id, CancellationToken ct = default)
        {
            await _api.PostAsync<object, object?>(
                $"api/admin/tickets/statuses/{id}/deactivate",
                new { },
                ct);
        }

        public async Task<TicketTaskCategoryDto> CreateTaskCategoryAsync(CreateTicketTaskCategoryRequest request, CancellationToken ct = default)
        {
            return await _api.PostAsync<CreateTicketTaskCategoryRequest, TicketTaskCategoryDto>(
                       "api/admin/tickets/task-categories", request, ct)
                   ?? new TicketTaskCategoryDto();
        }

        public async Task UpdateTaskCategoryAsync(UpdateTicketTaskCategoryRequest request, CancellationToken ct = default)
        {
            await _api.PutAsync<UpdateTicketTaskCategoryRequest>(
                $"api/admin/tickets/task-categories/{request.Id}",
                request,
                ct);
        }

        public async Task DeactivateTaskCategoryAsync(ulong id, CancellationToken ct = default)
        {
            await _api.PostAsync<object, object?>(
                $"api/admin/tickets/task-categories/{id}/deactivate",
                new { },
                ct);
        }

        public async Task DeleteStatusAsync(ulong id, CancellationToken ct = default)
        {
            await _api.PostAsync<object, object?>(
                $"api/admin/tickets/statuses/{id}/delete",
                new { },
                ct);
        }

        public async Task ReorderStatusesAsync(IEnumerable<ulong> orderedIds, CancellationToken ct = default)
        {
            await _api.PutAsync<ReorderTicketStatusesRequest>(
                "api/admin/tickets/statuses/reorder",
                new ReorderTicketStatusesRequest
                {
                    OrderedIds = orderedIds.ToList()
                },
                ct);
        }
    }
}