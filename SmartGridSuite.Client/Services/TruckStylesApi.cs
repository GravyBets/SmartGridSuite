using SmartGridSuite.Contracts.Trucks;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Services
{
    public sealed class TruckStylesApi
    {
        private readonly ApiClient _api;

        public TruckStylesApi(ApiClient api) => _api = api;

        public async Task<List<TruckStyleDto>> GetTruckStylesAsync(bool includeInactive = false, CancellationToken ct = default)
        {
            var path = $"api/truckstyles?includeInactive={includeInactive.ToString().ToLower()}";
            return await _api.GetAsync<List<TruckStyleDto>>(path, ct) ?? new();
        }

        public Task<TruckStyleDto?> CreateTruckStyleAsync(CreateTruckStyleRequest req, CancellationToken ct = default)
        {
            return _api.PostAsync<CreateTruckStyleRequest, TruckStyleDto>("api/truckstyles", req, ct);
        }
    }
}