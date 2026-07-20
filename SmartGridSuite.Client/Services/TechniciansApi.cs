using SmartGridSuite.Contracts.Administration.Technicians;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Services
{
    public sealed class TechniciansApi
    {
        private readonly ApiClient _api;

        public TechniciansApi(ApiClient api) => _api = api;

        public async Task<List<TechnicianDto>> GetTechniciansAsync(bool includeInactive = true, CancellationToken ct = default)
        {
            var path = $"api/technicians?includeInactive={includeInactive.ToString().ToLower()}";
            return await _api.GetAsync<List<TechnicianDto>>(path, ct) ?? new();
        }

        public async Task<long> CreateTechnicianAsync(CreateTechnicianRequest req, CancellationToken ct = default)
        {
            var res = await _api.PostAsync<CreateTechnicianRequest, CreateTechnicianResponse>("api/technicians", req, ct);
            return res?.Id ?? 0;
        }

        public Task UpdateTechnicianAsync(long id, UpdateTechnicianRequest req, CancellationToken ct = default)
        {
            return _api.PutAsync($"api/technicians/{id}", req, ct);
        }

        public async Task DeleteTechnicianAsync(int id, CancellationToken ct = default)
        {
            await _api.DeleteAsync(
                $"api/technicians/{id}",
                ct);
        }
    }
}