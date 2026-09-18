using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace SmartGridSuite.Api.Services.ParentSync
{
    /// <summary>
    /// Periodically verifies that the API can still open a live Parent DB
    /// connection. A failed probe uses the existing ParentDatabaseHealthService
    /// recovery path, which clears SqlClient connection pools, then retries once.
    /// </summary>
    public sealed class ParentDatabaseHeartbeatHostedService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval =
            TimeSpan.FromMinutes(5);

        private static readonly TimeSpan ProbeTimeout =
            TimeSpan.FromSeconds(5);

        private const string HeartbeatOperation =
            "Automatic Parent DB heartbeat";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ParentDatabaseHealthService _parentDatabaseHealth;
        private readonly ILogger<ParentDatabaseHeartbeatHostedService> _logger;

        public ParentDatabaseHeartbeatHostedService(
            IServiceScopeFactory scopeFactory,
            ParentDatabaseHealthService parentDatabaseHealth,
            ILogger<ParentDatabaseHeartbeatHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _parentDatabaseHealth = parentDatabaseHealth;
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
                // Normal API shutdown.
            }
        }

        private async Task RunHeartbeatCycleAsync(
            CancellationToken stoppingToken)
        {
            var firstFailure =
                await ProbeOnceAsync(stoppingToken);

            if (firstFailure is null)
            {
                _parentDatabaseHealth.RecordSuccess(
                    HeartbeatOperation);

                return;
            }

            /*
             * RecordFailure clears every SqlClient connection pool. This is the
             * lightweight equivalent of the useful part of an API restart for
             * stale/broken pooled Parent DB connections.
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
                _parentDatabaseHealth.RecordSuccess(
                    HeartbeatOperation + " recovery retry");

                return;
            }

            _parentDatabaseHealth.RecordFailure(
                retryFailure,
                HeartbeatOperation + " recovery retry");

            _logger.LogWarning(
                retryFailure,
                "Parent DB heartbeat recovery retry failed. " +
                "Cached Parent DB data will remain in use until a later " +
                "heartbeat or live lookup succeeds.");
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
