#nullable enable

using SmartGridSuite.Contracts.Administration.ConnectedClients;
using System.Security.Principal;

namespace SmartGridSuite.Client.Services
{
    public static class ClientPresenceService
    {
        private static readonly ApiClient Api =
            ClientAppSettings.CreateApiClient();

        private static readonly SemaphoreSlim SendLock =
            new(1, 1);

        private static readonly object StateLock =
            new();

        private static CancellationTokenSource? _cts;
        private static Task? _heartbeatLoopTask;

        private static string _currentModule =
            "Module Launcher";

        public static void Start(
            string initialModule = "Module Launcher")
        {
            lock (StateLock)
            {
                _currentModule =
                    NormalizeModuleName(initialModule);

                if (_cts != null)
                    return;

                _cts =
                    new CancellationTokenSource();

                _heartbeatLoopTask =
                    RunHeartbeatLoopAsync(
                        _cts.Token);
            }

            /*
             * Do not wait a full minute for the first heartbeat.
             * As soon as SmartGridSuite opens, register this client.
             */
            QueueImmediateHeartbeat();
        }

        public static void SetCurrentModule(
            string moduleName)
        {
            var cleanModule =
                NormalizeModuleName(moduleName);

            lock (StateLock)
            {
                if (string.Equals(
                        _currentModule,
                        cleanModule,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _currentModule =
                    cleanModule;
            }

            /*
             * Module changes are important enough to report immediately
             * instead of waiting for the next 60-second heartbeat.
             */
            QueueImmediateHeartbeat();
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;

            lock (StateLock)
            {
                cts = _cts;

                _cts = null;
                _heartbeatLoopTask = null;
            }

            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
                // Application shutdown must never be blocked by presence cleanup.
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static async Task RunHeartbeatLoopAsync(
            CancellationToken ct)
        {
            try
            {
                using var timer =
                    new PeriodicTimer(
                        TimeSpan.FromMinutes(1));

                while (await timer.WaitForNextTickAsync(ct))
                {
                    await SendHeartbeatSafeAsync(ct);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
        }

        private static void QueueImmediateHeartbeat()
        {
            CancellationToken ct;

            lock (StateLock)
            {
                if (_cts == null)
                    return;

                ct = _cts.Token;
            }

            _ = SendHeartbeatSafeAsync(ct);
        }

        private static async Task SendHeartbeatSafeAsync(
            CancellationToken ct)
        {
            try
            {
                await SendHeartbeatAsync(ct);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (ApiClient.ApiConnectionException)
            {
                /*
                 * Presence is best-effort.
                 *
                 * A weak VPN/cellular connection should never interrupt
                 * the technician just because the heartbeat could not
                 * reach the API.
                 */
            }
            catch (ApiClient.ApiException)
            {
                // Presence failures should remain non-blocking.
            }
            catch
            {
                // Never surface heartbeat errors into normal application use.
            }
        }

        private static async Task SendHeartbeatAsync(
            CancellationToken ct)
        {
            await SendLock.WaitAsync(ct);

            try
            {
                var technician =
                    CurrentUserService.CurrentTechnician;

                /*
                 * The launcher may send its first heartbeat before a role
                 * check has loaded the current technician.
                 *
                 * Resolve the identity once here if necessary. The
                 * CurrentUserService will cache the successful result.
                 */
                if (technician == null)
                {
                    try
                    {
                        technician =
                            await CurrentUserService
                                .LoadCurrentTechnicianAsync(
                                    forceRefresh: false,
                                    ct);
                    }
                    catch
                    {
                        /*
                         * We can still send machine/version presence even
                         * if the technician lookup itself failed.
                         */
                    }
                }

                string currentModule;

                lock (StateLock)
                {
                    currentModule =
                        _currentModule;
                }

                var request =
                    new ClientHeartbeatRequest
                    {
                        EmployeeId =
                            CurrentUserService
                                .CurrentEmployeeId,

                        DisplayName =
                            technician?.Name
                            ?? string.Empty,

                        WindowsUser =
                            GetWindowsIdentityName(),

                        MachineName =
                            Environment.MachineName,

                        ClientVersion =
                            ClientVersionService
                                .GetInstalledVersionText(),

                        CurrentModule =
                            currentModule
                    };

                /*
                 * The heartbeat endpoint returns 204 No Content.
                 * ApiClient's generic POST already handles NoContent by
                 * returning default, so no additional API method is needed.
                 */
                await Api.PostAsync<
                    ClientHeartbeatRequest,
                    object>(
                        "api/client-presence/heartbeat",
                        request,
                        ct);
            }
            finally
            {
                SendLock.Release();
            }
        }

        private static string GetWindowsIdentityName()
        {
            try
            {
                var name =
                    WindowsIdentity
                        .GetCurrent()?
                        .Name;

                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch
            {
                // Fall through to Environment-based identity.
            }

            var domain =
                Environment.UserDomainName
                ?? string.Empty;

            var user =
                Environment.UserName
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(domain))
                return user.Trim();

            return
                $"{domain.Trim()}\\{user.Trim()}";
        }

        private static string NormalizeModuleName(
            string? moduleName)
        {
            var clean =
                (moduleName ?? string.Empty)
                    .Trim();

            return string.IsNullOrWhiteSpace(clean)
                ? "Module Launcher"
                : clean;
        }
    }
}