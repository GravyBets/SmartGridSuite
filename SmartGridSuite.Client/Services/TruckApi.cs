using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartGridSuite.Contracts.Administration.Trucks;

namespace SmartGridSuite.Client.Services
{
    public sealed class TrucksApi
    {
        private readonly ApiClient _api;

        public TrucksApi(ApiClient api) => _api = api;

        public async Task<List<TruckDto>> GetTrucksAsync(CancellationToken ct = default)
        {
            return await _api.GetAsync<List<TruckDto>>("api/trucks", ct) ?? new();
        }

        public async Task<TruckDto?> CreateTruckAsync(CreateTruckRequest req, CancellationToken ct = default)
        {
            return await _api.PostAsync<CreateTruckRequest, TruckDto>("api/trucks", req, ct);
        }

        public Task UpdateTruckAsync(int truckId, UpdateTruckRequest req, CancellationToken ct = default)
        {
            return _api.PutAsync($"api/trucks/{truckId}", req, ct);
        }
    }
}