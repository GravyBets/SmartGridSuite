using SmartGridSuite.Contracts.Versioning;
using System.Reflection;

namespace SmartGridSuite.Client.Services
{
    public enum ClientVersionState
    {
        Unknown,
        Current,
        UpdateAvailable,
        Unsupported
    }

    public sealed class ClientVersionCheckResult
    {
        public ClientVersionState State { get; init; }

        public string InstalledVersion { get; init; } = "";

        public string LatestVersion { get; init; } = "";

        public string MinimumSupportedVersion { get; init; } = "";

        public string Message { get; init; } = "";

        public ClientVersionDto? ServerVersion { get; init; }
    }

    public sealed class ClientVersionService
    {
        private readonly ApiClient _apiClient =
            ClientAppSettings.CreateApiClient();

        private ClientVersionCheckResult? _cachedResult;

        public static ClientVersionService Current { get; } =
            new ClientVersionService();

        private ClientVersionService()
        {
        }

        public static string GetInstalledVersionText()
        {
            return GetInstalledVersion().ToString();
        }

        public async Task<ClientVersionCheckResult> CheckAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedResult != null)
                return _cachedResult;

            var installedVersion = GetInstalledVersion();

            try
            {
                var serverVersion =
                    await _apiClient.GetAsync<ClientVersionDto>(
                        "api/system/client-version");

                if (serverVersion == null)
                {
                    return CacheUnknown(
                        installedVersion,
                        "The server returned no version information.");
                }

                if (!Version.TryParse(
                        serverVersion.LatestVersion,
                        out var latestVersion))
                {
                    return CacheUnknown(
                        installedVersion,
                        "The server returned an invalid latest version.");
                }

                if (!Version.TryParse(
                        serverVersion.MinimumSupportedVersion,
                        out var minimumVersion))
                {
                    return CacheUnknown(
                        installedVersion,
                        "The server returned an invalid minimum supported version.");
                }

                ClientVersionState state;
                string message;

                if (installedVersion.CompareTo(minimumVersion) < 0)
                {
                    state = ClientVersionState.Unsupported;
                    message =
                        "This version is no longer supported. " +
                        "Close and reopen Smart Grid Suite to install the required update.";
                }
                else if (installedVersion.CompareTo(latestVersion) < 0)
                {
                    state = ClientVersionState.UpdateAvailable;
                    message =
                        "A newer version is available. " +
                        "Close and reopen Smart Grid Suite to install the update.";
                }
                else
                {
                    state = ClientVersionState.Current;
                    message =
                        "You are running the latest version.";
                }

                _cachedResult = new ClientVersionCheckResult
                {
                    State = state,
                    InstalledVersion = installedVersion.ToString(),
                    LatestVersion = latestVersion.ToString(),
                    MinimumSupportedVersion = minimumVersion.ToString(),
                    Message = message,
                    ServerVersion = serverVersion
                };

                return _cachedResult;
            }
            catch (ApiClient.ApiConnectionException)
            {
                return CacheUnknown(
                    installedVersion,
                    "Version status could not be checked because the server is unavailable.");
            }
            catch (ApiClient.ApiException ex)
            {
                return CacheUnknown(
                    installedVersion,
                    $"Version status could not be checked. Server error: {ex.StatusCode}.");
            }
            catch (Exception ex)
            {
                return CacheUnknown(
                    installedVersion,
                    $"Version status could not be checked: {ex.Message}");
            }
        }

        private ClientVersionCheckResult CacheUnknown(Version installedVersion, string message)
        {
            _cachedResult = new ClientVersionCheckResult
            {
                State = ClientVersionState.Unknown,
                InstalledVersion = installedVersion.ToString(),
                Message = message
            };

            return _cachedResult;
        }

        private static Version GetInstalledVersion()
        {
            var clickOnceVersionText =
                Environment.GetEnvironmentVariable(
                    "ClickOnce_CurrentVersion");

            if (Version.TryParse(
                    clickOnceVersionText,
                    out var clickOnceVersion))
            {
                return clickOnceVersion;
            }

            var assembly =
                Assembly.GetEntryAssembly()
                ?? typeof(ClientVersionService).Assembly;

            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            var normalizedInformationalVersion =
                RemoveVersionMetadata(informationalVersion);

            if (Version.TryParse(
                    normalizedInformationalVersion,
                    out var applicationVersion))
            {
                return applicationVersion;
            }

            return assembly.GetName().Version
                   ?? new Version(0, 0, 0, 0);
        }

        private static string? RemoveVersionMetadata(string? versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
                return null;

            var metadataIndex =
                versionText.IndexOf('+');

            if (metadataIndex >= 0)
            {
                versionText =
                    versionText[..metadataIndex];
            }

            var prereleaseIndex =
                versionText.IndexOf('-');

            if (prereleaseIndex >= 0)
            {
                versionText =
                    versionText[..prereleaseIndex];
            }

            return versionText;
        }
    }
}