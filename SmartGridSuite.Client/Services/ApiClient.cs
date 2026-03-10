using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

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
                try { text = await resp.Content.ReadAsStringAsync(ct); } catch { }

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
                try { text = await resp.Content.ReadAsStringAsync(ct); } catch { }

                throw new ApiException((int)resp.StatusCode, text);
            }
        }
    }
}