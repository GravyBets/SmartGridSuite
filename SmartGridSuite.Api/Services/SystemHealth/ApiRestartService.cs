using System.Diagnostics;
using SmartGridSuite.Contracts.Administration;

namespace SmartGridSuite.Api.Services.SystemHealth
{
    public sealed class ApiRestartService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationRuntimeHealthService _runtime;
        private readonly ILogger<ApiRestartService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _attemptWindowUtc;
        private int _attempts;
        private DateTime _lastAcceptedUtc;

        public ApiRestartService(IConfiguration configuration,
            ApplicationRuntimeHealthService runtime, ILogger<ApiRestartService> logger)
        {
            _configuration = configuration;
            _runtime = runtime;
            _logger = logger;
        }

        public async Task<(int Status, RestartApiResponse Response)> RequestAsync(string? password)
        {
            if (!await _gate.WaitAsync(0))
                return Failure(429, "Another restart request is being processed.");

            try
            {
                if (!OperatingSystem.IsLinux() ||
                    !_configuration.GetValue<bool>("AdminMaintenance:RestartEnabled") ||
                    string.IsNullOrWhiteSpace(_configuration["AdminMaintenance:RestartPasswordHash"]))
                    return Failure(503, "API restart has not been configured on this server.");

                var now = DateTime.UtcNow;
                if (now - _attemptWindowUtc >= TimeSpan.FromMinutes(1))
                {
                    _attemptWindowUtc = now;
                    _attempts = 0;
                }
                if (++_attempts > 5)
                    return Failure(429, "Too many restart attempts. Wait one minute and try again.");

                if (!RestartPassword.Verify(password,
                        _configuration["AdminMaintenance:RestartPasswordHash"]))
                {
                    _logger.LogWarning("API restart rejected: invalid administrator password.");
                    return Failure(401, "Incorrect administrator restart password.");
                }

                if (now - _lastAcceptedUtc < TimeSpan.FromMinutes(1))
                    return Failure(409, "A restart was already requested. Wait for the API to return.");

                // No request-supplied executable, arguments, shell text, or password.
                // The root-owned helper detaches from this process before restarting SysV.
                var info = new ProcessStartInfo("/usr/bin/sudo")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                info.ArgumentList.Add("-n");
                info.ArgumentList.Add("/usr/local/sbin/smartgridsuite-restart-api");
                using var process = Process.Start(info)
                    ?? throw new InvalidOperationException("Restart helper could not start.");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(output, error);
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("API restart helper failed with exit code {ExitCode}.", process.ExitCode);
                    return Failure(503, "Restart helper could not queue the restart. Check the VM maintenance setup.");
                }

                _lastAcceptedUtc = DateTime.UtcNow;
                _logger.LogWarning("Administrator API restart accepted.");
                return (202, new RestartApiResponse
                {
                    Accepted = true,
                    PreviousStartedAtUtc = _runtime.StartedAtUtc.UtcDateTime,
                    Message = "Restart queued. Wait for a new API start time before treating it as complete."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to confirm that the API restart was queued.");
                return Failure(503, "Could not confirm the restart request. Refresh health before trying again.");
            }
            finally
            {
                _gate.Release();
            }
        }

        private static (int, RestartApiResponse) Failure(int status, string message)
            => (status, new RestartApiResponse { Message = message });
    }
}
