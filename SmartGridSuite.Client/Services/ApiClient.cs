using SmartGridSuite.Contracts.Settings;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Administration;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace SmartGridSuite.Client.Services
{
    public sealed class ApiClient
    {
        private readonly HttpClient _http;

        public ApiClient(string baseUrl)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(
                baseUrl.EndsWith("/")
                    ? baseUrl
                    : baseUrl + "/"),

                /*
                 * Prevent a weak or missing field connection from leaving the UI waiting
                 * indefinitely for an API response.
                 */
                Timeout = TimeSpan.FromSeconds(15)
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

        // Represents a failure to contact the API, including network loss,
        // connection refusal, DNS failure, and request timeout.
        public sealed class ApiConnectionException : Exception
        {
            public string RequestPath { get; }

            public bool IsTimeout { get; }

            public ApiConnectionException(
                string message,
                string requestPath,
                bool isTimeout,
                Exception innerException)
                : base(message, innerException)
            {
                RequestPath = requestPath;
                IsTimeout = isTimeout;
            }
        }

        // Sends a GET request through the shared connection/error handling path so
        // callers receive a predictable exception when the network or API is unavailable.
        public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                path,
                content: null,
                ct);

            return await ReadJsonOrDefaultAsync<T>(response, ct);
        }

        // Sends a JSON POST request while distinguishing API validation failures
        // from connectivity and timeout failures.
        public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Post,
                path,
                JsonContent.Create(body),
                ct);

            return await ReadJsonOrDefaultAsync<TResponse>(
                response,
                ct);
        }

        // Sends a JSON PUT request that does not require a response body.
        public async Task PutAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Put,
                path,
                JsonContent.Create(body),
                ct);
        }

        public async Task DeleteAsync(string path, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Delete,
                path,
                content: null,
                ct);
        }

        // Sends a JSON PUT request and deserializes the optional response body.
        public async Task<TResponse?> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Put,
                path,
                JsonContent.Create(body),
                ct);

            return await ReadJsonOrDefaultAsync<TResponse>(
                response,
                ct);
        }

        // Loads one Site Dashboard through the centralized API connection handling.
        public async Task<SiteDashboardResponseDto?> GetSiteDashboardAsync(string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return null;

            return await GetAsync<SiteDashboardResponseDto>(
                $"api/site-dashboard/{Uri.EscapeDataString(siteId)}",
                cancellationToken);
        }

        // Loads Site Dashboard routing information through the centralized API client.
        public async Task<SiteDashboardRouteInfoDto?> GetSiteDashboardRouteInfoAsync(string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return null;

            return await GetAsync<SiteDashboardRouteInfoDto>(
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

        public async Task DeleteCommunicationDeviceTypeAsync(uint id, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Delete,
                $"api/admin/general-settings/communication-device-types/{id}",
                content: null,
                ct);
        }

        public async Task<List<WriteUpFlagDto>> GetWriteUpFlagsAsync(
            bool activeOnly = false,
            bool technicianVisibleOnly = false)
        {
            var url =
                "api/admin/general-settings/write-up-flags" +
                $"?activeOnly={activeOnly.ToString().ToLowerInvariant()}" +
                $"&technicianVisibleOnly={technicianVisibleOnly.ToString().ToLowerInvariant()}";

            return await GetAsync<List<WriteUpFlagDto>>(url)
                   ?? new List<WriteUpFlagDto>();
        }

        public async Task<WriteUpFlagDto?> CreateWriteUpFlagAsync(
            SaveWriteUpFlagRequest request)
        {
            return await PostAsync<SaveWriteUpFlagRequest, WriteUpFlagDto>(
                "api/admin/general-settings/write-up-flags",
                request);
        }

        public async Task<WriteUpFlagDto?> UpdateWriteUpFlagAsync(
            uint id,
            SaveWriteUpFlagRequest request)
        {
            return await PutAsync<SaveWriteUpFlagRequest, WriteUpFlagDto>(
                $"api/admin/general-settings/write-up-flags/{id}",
                request);
        }

        public async Task DeleteWriteUpFlagAsync(
            uint id, CancellationToken ct = default)
        {
            using var response = await SendAsync(
                HttpMethod.Delete,
                $"api/admin/general-settings/write-up-flags/{id}",
                content: null,
                ct);
        }

        // -------------------------
        // Refer To Options
        // -------------------------

        public async Task<List<ReferToOptionDto>> GetReferToOptionsAsync(
            bool activeOnly = false)
        {
            var url =
                "api/admin/general-settings/refer-to-options" +
                $"?activeOnly={activeOnly.ToString().ToLowerInvariant()}";

            return await GetAsync<List<ReferToOptionDto>>(url)
                   ?? new List<ReferToOptionDto>();
        }

        public async Task<ReferToOptionDto?> CreateReferToOptionAsync(
            SaveReferToOptionRequest request)
        {
            return await PostAsync<
                SaveReferToOptionRequest,
                ReferToOptionDto>(
                "api/admin/general-settings/refer-to-options",
                request);
        }

        public async Task<ReferToOptionDto?> UpdateReferToOptionAsync(
            uint id,
            SaveReferToOptionRequest request)
        {
            return await PutAsync<
                SaveReferToOptionRequest,
                ReferToOptionDto>(
                $"api/admin/general-settings/refer-to-options/{id}",
                request);
        }

        public async Task DeleteReferToOptionAsync(
            uint id,
            CancellationToken ct = default)
        {
            await DeleteAsync(
                $"api/admin/general-settings/refer-to-options/{id}",
                ct);
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

        // -------------------------
        // Dispatch Closeout Checklist Definitions
        // -------------------------

        public async Task<List<DispatchCloseoutChecklistDefinitionDto>>
            GetDispatchCloseoutChecklistDefinitionsAsync(
                bool activeOnly = false)
        {
            var url =
                "api/admin/general-settings/" +
                "dispatch-closeout-checklist-definitions" +
                $"?activeOnly={activeOnly.ToString().ToLowerInvariant()}";

            return await GetAsync<
                       List<DispatchCloseoutChecklistDefinitionDto>>(url)
                   ?? new List<DispatchCloseoutChecklistDefinitionDto>();
        }

        public async Task<DispatchCloseoutChecklistDefinitionDto?>
            CreateDispatchCloseoutChecklistDefinitionAsync(
                SaveDispatchCloseoutChecklistDefinitionRequest request)
        {
            return await PostAsync<
                SaveDispatchCloseoutChecklistDefinitionRequest,
                DispatchCloseoutChecklistDefinitionDto>(
                "api/admin/general-settings/" +
                "dispatch-closeout-checklist-definitions",
                request);
        }

        public async Task<DispatchCloseoutChecklistDefinitionDto?>
            UpdateDispatchCloseoutChecklistDefinitionAsync(
                uint id,
                SaveDispatchCloseoutChecklistDefinitionRequest request)
        {
            return await PutAsync<
                SaveDispatchCloseoutChecklistDefinitionRequest,
                DispatchCloseoutChecklistDefinitionDto>(
                "api/admin/general-settings/" +
                $"dispatch-closeout-checklist-definitions/{id}",
                request);
        }

        public async Task DeleteDispatchCloseoutChecklistDefinitionAsync(
            uint id,
            CancellationToken ct = default)
        {
            await DeleteAsync(
                "api/admin/general-settings/" +
                $"dispatch-closeout-checklist-definitions/{id}",
                ct);
        }

        // -------------------------
        // Admin Email Settings
        // -------------------------

        public async Task<EmailSettingsDto?> GetEmailSettingsAsync(CancellationToken ct = default)
        {
            return await GetAsync<EmailSettingsDto>(
                "api/admin/email-settings",
                ct);
        }

        public async Task<EmailSettingsDto?> UpdateEmailSettingsAsync(UpdateEmailSettingsRequest request, CancellationToken ct = default)
        {
            return await PostAsync<UpdateEmailSettingsRequest, EmailSettingsDto>(
                "api/admin/email-settings",
                request,
                ct);
        }

        public async Task<SendTestEmailResponse?> SendTestEmailAsync(SendTestEmailRequest request, CancellationToken ct = default)
        {
            return await PostAsync<SendTestEmailRequest, SendTestEmailResponse>(
                "api/admin/email-settings/test",
                request,
                ct);
        }

        public async Task<SubmitBugFeatureResponse?> SubmitBugFeatureRequestAsync(SubmitBugFeatureRequest request, CancellationToken ct = default)
        {
            return await PostAsync<
                SubmitBugFeatureRequest,
                SubmitBugFeatureResponse>(
                    "api/support-requests/bug-feature",
                    request,
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

        public Task<SystemHealthDto?> GetSystemHealthAsync(CancellationToken cancellationToken = default)
        {
            return GetAsync<SystemHealthDto>(
                "api/admin/system-health",
                cancellationToken);
        }

        // Executes every API request through one connection/error boundary so weak or
        // missing field connectivity cannot surface as inconsistent raw HTTP exceptions.
        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content,
            CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = content
            };

            try
            {
                var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                /*
                 * Any HTTP response proves that the API is reachable. Validation and server
                 * errors are handled separately from actual loss of connectivity.
                 */
                ConnectivityService.ReportOnline();

                if (response.IsSuccessStatusCode)
                    return response;

                var errorBody = await ReadErrorBodyAsync(
                    response,
                    ct);

                var statusCode = (int)response.StatusCode;

                response.Dispose();

                throw new ApiException(
                    statusCode,
                    errorBody);
            }
            catch (TaskCanceledException ex)
                when (!ct.IsCancellationRequested)
            {
                ConnectivityService.ReportOffline(
                    "The Smart Grid Suite server did not respond before the request timed out.");

                throw new ApiConnectionException(
                    "The Smart Grid Suite server did not respond before the request timed out.",
                    path,
                    isTimeout: true,
                    ex);
            }
            catch (HttpRequestException ex)
            {
                ConnectivityService.ReportOffline(
                    "Offline — showing previously loaded data. Check the network connection and retry.");

                throw new ApiConnectionException(
                    "Unable to reach the Smart Grid Suite server. Check the network connection and try again.",
                    path,
                    isTimeout: false,
                    ex);
            }
        }

        // Safely reads a server-provided validation or error message without allowing
        // a secondary content-read failure to hide the original HTTP status.
        private static async Task<string?> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                var text = await response.Content
                    .ReadAsStringAsync(ct);

                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : text.Trim().Trim('"');
            }
            catch
            {
                return null;
            }
        }

        private static async Task<TResponse?> ReadJsonOrDefaultAsync<TResponse>(HttpResponseMessage resp, CancellationToken ct)
        {
            if (resp.StatusCode == HttpStatusCode.NoContent)
                return default;

            var text = await resp.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(text))
                return default;

            return JsonSerializer.Deserialize<TResponse>(
                text,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
    }
}