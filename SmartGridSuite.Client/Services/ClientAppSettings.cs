using System;
using System.Net.Http;

namespace SmartGridSuite.Client.Services
{
    public static class ClientAppSettings
    {
        public static string ApiBaseUrl { get; set; } = "https://localhost:7140/";

        public static Uri ApiBaseUri =>
            new Uri(ApiBaseUrl.EndsWith("/")
                ? ApiBaseUrl
                : ApiBaseUrl + "/");

        public static ApiClient CreateApiClient()
        {
            return new ApiClient(ApiBaseUrl);
        }

        public static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                BaseAddress = ApiBaseUri,
                Timeout = TimeSpan.FromSeconds(15)
            };
        }
    }
}