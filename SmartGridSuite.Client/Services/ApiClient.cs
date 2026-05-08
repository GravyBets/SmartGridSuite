using SmartGridSuite.Contracts.Settings;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Net.Http;
using System.Net.Http.Json;

namespace SmartGridSuite.Client.Services
{
    public sealed class ApiClient
    {
        private readonly HttpClient _http;

        public ApiClient(string baseUrl)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/")
            };
        }

        public sealed class ApiException : Exception
        {
            public int StatusCode { get; }
            public string? Body { get; }

            public ApiException(int statusCode, string? body)
                : base(body ?? $"API request failed with status {statusCode}.")
            {
                StatusCode = statusCode;
                Body = body;
            }
        }

        public Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
            => _http.GetFromJsonAsync<T>(path, ct);

        public async Task<TResponse?> PostAsync<TRequest, TResponse>(
            string path,
            TRequest body,
            CancellationToken ct = default)
        {
            using var resp = await _http.PostAsJsonAsync(path, body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string? text = null;
                try
                {
                    text = await resp.Content.ReadAsStringAsync(ct);
                }
                catch
                {
                }

                throw new ApiException((int)resp.StatusCode, text);
            }

            return await resp.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }

        public async Task PutAsync<TRequest>(
            string path,
            TRequest body,
            CancellationToken ct = default)
        {
            using var resp = await _http.PutAsJsonAsync(path, body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string? text = null;
                try
                {
                    text = await resp.Content.ReadAsStringAsync(ct);
                }
                catch
                {
                }

                throw new ApiException((int)resp.StatusCode, text);
            }
        }

        public async Task<TResponse?> PutAsync<TRequest, TResponse>(
            string path,
            TRequest body,
            CancellationToken ct = default)
        {
            using var resp = await _http.PutAsJsonAsync(path, body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string? text = null;
                try
                {
                    text = await resp.Content.ReadAsStringAsync(ct);
                }
                catch
                {
                }

                throw new ApiException((int)resp.StatusCode, text);
            }

            return await resp.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
        }

        public async Task<SiteDashboardResponseDto?> GetSiteDashboardAsync(
            string siteId,
            CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return null;

            return await _http.GetFromJsonAsync<SiteDashboardResponseDto>(
                $"api/site-dashboard/{Uri.EscapeDataString(siteId)}",
                cancellationToken);
        }

        public async Task<SiteDashboardRouteInfoDto?> GetSiteDashboardRouteInfoAsync(
            string siteId,
            CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return null;

            return await _http.GetFromJsonAsync<SiteDashboardRouteInfoDto>(
                $"api/site-dashboard/site-dashboard-route/{Uri.EscapeDataString(siteId)}",
                cancellationToken);
        }

        public Task<IgsdPortalUrlDto?> GetIgsdPortalUrlAsync(CancellationToken ct = default)
        {
            return GetAsync<IgsdPortalUrlDto>("api/app-settings/igsd-portal-url", ct);
        }

        public Task<IgsdPortalUrlDto?> UpdateIgsdPortalUrlAsync(
            string url,
            CancellationToken ct = default)
        {
            return PutAsync<UpdateIgsdPortalUrlRequest, IgsdPortalUrlDto>(
                "api/app-settings/igsd-portal-url",
                new UpdateIgsdPortalUrlRequest
                {
                    Url = url ?? string.Empty
                },
                ct);
        }

        public async Task<List<CommunicationDeviceTypeDto>> GetCommunicationDeviceTypesAsync(bool activeOnly = false)
        {
            var url = $"api/admin/general-settings/communication-device-types?activeOnly={activeOnly.ToString().ToLowerInvariant()}";

            return await GetAsync<List<CommunicationDeviceTypeDto>>(url)
                   ?? new List<CommunicationDeviceTypeDto>();
        }

        public async Task<CommunicationDeviceTypeDto?> CreateCommunicationDeviceTypeAsync(
            SaveCommunicationDeviceTypeRequest request)
        {
            return await PostAsync<SaveCommunicationDeviceTypeRequest, CommunicationDeviceTypeDto>(
                "api/admin/general-settings/communication-device-types",
                request);
        }

        public async Task<CommunicationDeviceTypeDto?> UpdateCommunicationDeviceTypeAsync(
            uint id,
            SaveCommunicationDeviceTypeRequest request)
        {
            return await PutAsync<SaveCommunicationDeviceTypeRequest, CommunicationDeviceTypeDto>(
                $"api/admin/general-settings/communication-device-types/{id}",
                request);
        }

        public async Task<RangeExtenderLinkUrlDto?> GetRangeExtenderLinkUrlAsync(
            CancellationToken ct = default)
        {
            return await GetAsync<RangeExtenderLinkUrlDto>(
                "api/app-settings/range-extender-link-url",
                ct);
        }

        public async Task<RangeExtenderLinkUrlDto?> UpdateRangeExtenderLinkUrlAsync(
            string url, CancellationToken ct = default)
        {
            return await PutAsync<UpdateRangeExtenderLinkUrlRequest, RangeExtenderLinkUrlDto>(
                "api/app-settings/range-extender-link-url",
                new UpdateRangeExtenderLinkUrlRequest
                {
                    Url = url ?? string.Empty
                },
                ct);
        }

        //Tower Stuff
        public async Task<List<TowerSearchResultDto>> SearchTowersAsync(
            string term, int take = 25, CancellationToken ct = default)
        {
            term = (term ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(term))
                return new List<TowerSearchResultDto>();

            return await GetAsync<List<TowerSearchResultDto>>(
                $"api/site-dashboard/tower-search?term={Uri.EscapeDataString(term)}&take={take}",
                ct) ?? new List<TowerSearchResultDto>();
        }

        public async Task<SiteDashboardResponseDto?> GetTowerDashboardAsync(
            int topNameId, CancellationToken ct = default)
        {
            return await GetAsync<SiteDashboardResponseDto>(
                $"api/site-dashboard/tower-dashboard/{topNameId}",
                ct);
        }
    }
}