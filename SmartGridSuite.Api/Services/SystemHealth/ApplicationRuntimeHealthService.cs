using System.Diagnostics;
using System.Reflection;

namespace SmartGridSuite.Api.Services.SystemHealth
{
    public sealed class ApplicationRuntimeHealthService
    {
        public ApplicationRuntimeHealthService(IConfiguration configuration)
        {
            StartedAtUtc =
                Process.GetCurrentProcess()
                    .StartTime
                    .ToUniversalTime();

            var assembly =
                Assembly.GetEntryAssembly() ??
                Assembly.GetExecutingAssembly();

            var configuredVersion = configuration["ClientVersion:LatestVersion"];

            ApiVersion =
                !string.IsNullOrWhiteSpace(configuredVersion)
                    ? configuredVersion
                    : assembly
                        .GetCustomAttribute<
                            AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion
                        ?.Split('+')[0]
                      ??
                      assembly.GetName().Version?.ToString()
                      ??
                      "Unknown";
        }

        public DateTimeOffset StartedAtUtc { get; }

        public string ApiVersion { get; }

        public TimeSpan Uptime => DateTimeOffset.UtcNow - StartedAtUtc;
    }
}