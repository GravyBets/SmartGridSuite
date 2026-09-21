using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using SmartGridSuite.Api.Services.SystemHealth;

namespace SmartGridSuite.Api.Services.ParentSync
{
    /// <summary>
    /// Periodically verifies that the API can still open a live Parent DB
    /// connection. A failed probe clears SqlClient pools and retries once.
    /// Repeated failures can escalate to one guarded API restart.
    /// </summary>
    public sealed class ParentDatabaseHeartbeatHostedService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval =
            TimeSpan.FromMinutes(5);

        private static readonly TimeSpan ProbeTimeout =
            TimeSpan.FromSeconds(5);

        private const int AutomaticRestartFailureThreshold = 3;

        private const string HeartbeatOperation =
            "Automatic Parent DB heartbeat";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ParentDatabaseHealthService _parentDatabaseHealth;
        private readonly ApiRestartService _apiRestartService;
        private readonly ILogger<ParentDatabaseHeartbeatHostedService> _logger;

        private int _consecutiveFailedCycles;
        private bool _hasObservedHealthyParentDatabase;
        private bool _automaticRestartQueued;

        public ParentDatabaseHeartbeatHostedService(
            IServiceScopeFactory scopeFactory,
            ParentDatabaseHealthService parentDatabaseHealth,
            ApiRestartService apiRestartService,
            ILogger<ParentDatabaseHeartbeatHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _parentDatabaseHealth = parentDatabaseHealth;
            _apiRestartService = apiRestartService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            try
            {
                // Check immediately at API startup instead of waiting five minutes.
                await RunHeartbeatCycleAsync(stoppingToken);

                using var timer =
                    new PeriodicTimer(CheckInterval);

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await RunHeartbeatCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                // Normal API shutdown/restart.
            }
        }

        private async Task RunHeartbeatCycleAsync(
            CancellationToken stoppingToken)
        {
            var firstFailure =
                await ProbeOnceAsync(stoppingToken);

            if (firstFailure is null)
            {
                RecordSuccessfulHeartbeat(
                    HeartbeatOperation);

                return;
            }

            /*
             * RecordFailure already clears every SqlClient connection pool.
             * That gives a broken/stale pooled connection one chance to recover
             * without interrupting the API for the entire shop.
             */
            _parentDatabaseHealth.RecordFailure(
                firstFailure,
                HeartbeatOperation);

            if (stoppingToken.IsCancellationRequested)
                return;

            _logger.LogInformation(
                "Parent DB heartbeat failed. SQL connection pools were cleared; " +
                "retrying once with a fresh connection.");

            var retryFailure =
                await ProbeOnceAsync(stoppingToken);

            if (retryFailure is null)
            {
                RecordSuccessfulHeartbeat(
                    HeartbeatOperation + " recovery retry");

                return;
            }

            _parentDatabaseHealth.RecordFailure(
                retryFailure,
                HeartbeatOperation + " recovery retry");

            _consecutiveFailedCycles++;

            _logger.LogWarning(
                retryFailure,
                "Parent DB heartbeat recovery retry failed. " +
                "Consecutive failed heartbeat cycles: {FailureCount}/{Threshold}. " +
                "Cached Parent DB data will remain in use.",
                _consecutiveFailedCycles,
                AutomaticRestartFailureThreshold);

            /*
             * Restart only when this API process previously demonstrated that
             * Parent DB was healthy. A process that STARTS while SQL/networking is
             * genuinely unavailable is therefore never allowed to restart-loop.
             *
             * Each failed cycle contains two failed probes (before and after pool
             * clearing), and cycles are five minutes apart.
             */
            if (!_hasObservedHealthyParentDatabase ||
                _automaticRestartQueued ||
                _consecutiveFailedCycles <
                    AutomaticRestartFailureThreshold)
            {
                return;
            }

            var queued =
                await _apiRestartService
                    .TryQueueAutomaticRestartAsync(
                        "Parent DB remained unavailable after " +
                        $"{_consecutiveFailedCycles} consecutive heartbeat " +
                        "cycles, including SQL pool-clear recovery retries.",
                        stoppingToken);

            if (!queued)
            {
                _logger.LogError(
                    "Parent DB automatic recovery reached the restart threshold, " +
                    "but the API restart could not be queued. The heartbeat will " +
                    "try recovery again on the next cycle.");

                return;
            }

            /*
             * Do not request another restart from this process while the detached
             * helper is waiting to restart the SysV service.
             */
            _automaticRestartQueued = true;
        }

        private void RecordSuccessfulHeartbeat(
            string operation)
        {
            _parentDatabaseHealth.RecordSuccess(
                operation);

            _hasObservedHealthyParentDatabase = true;
            _consecutiveFailedCycles = 0;
        }

        private async Task<Exception?> ProbeOnceAsync(
            CancellationToken stoppingToken)
        {
            try
            {
                using var timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken);

                timeout.CancelAfter(ProbeTimeout);

                using var scope =
                    _scopeFactory.CreateScope();

                var connectionFactory =
                    scope.ServiceProvider
                        .GetRequiredService<
                            ParentDatabaseConnectionFactory>();

                await using var connection =
                    connectionFactory.CreateConnection();

                await connection.OpenAsync(timeout.Token);

                await using var command =
                    new SqlCommand(
                        "SELECT 1;",
                        connection)
                    {
                        CommandTimeout =
                            (int)ProbeTimeout.TotalSeconds
                    };

                await command.ExecuteScalarAsync(
                    timeout.Token);

                return null;
            }
            catch (OperationCanceledException ex)
                when (!stoppingToken.IsCancellationRequested)
            {
                return new TimeoutException(
                    $"Parent DB heartbeat timed out after " +
                    $"{ProbeTimeout.TotalSeconds:0} seconds.",
                    ex);
            }
            catch (Exception ex)
                when (!stoppingToken.IsCancellationRequested)
            {
                return ex;
            }
        }
    }
}
