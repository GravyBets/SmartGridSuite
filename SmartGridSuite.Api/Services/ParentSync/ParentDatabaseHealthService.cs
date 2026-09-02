using Microsoft.Data.SqlClient;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed class ParentDatabaseHealthService
    {
        private readonly ILogger<ParentDatabaseHealthService>
            _logger;

        private readonly object _stateLock = new();

        private DateTimeOffset? _unavailableSinceUtc;

        public ParentDatabaseHealthService(
            ILogger<ParentDatabaseHealthService> logger)
        {
            _logger = logger;
        }

        public void RecordFailure(
            Exception exception,
            string operation)
        {
            /*
             * A network interruption can leave unusable SQL Server
             * connections inside SqlClient's connection pool.
             *
             * Clearing the pools forces the next live lookup to create
             * a completely new physical connection.
             */
            SqlConnection.ClearAllPools();

            var becameUnavailable = false;

            lock (_stateLock)
            {
                if (_unavailableSinceUtc is null)
                {
                    _unavailableSinceUtc =
                        DateTimeOffset.UtcNow;

                    becameUnavailable = true;
                }
            }

            if (!becameUnavailable)
                return;

            _logger.LogWarning(
                exception,
                "Parent database became unavailable during " +
                "{Operation}. SQL connection pools were cleared. " +
                "Cached data will be used while live lookups " +
                "continue retrying.",
                operation);
        }

        public void RecordSuccess(string operation)
        {
            DateTimeOffset? unavailableSinceUtc;

            lock (_stateLock)
            {
                unavailableSinceUtc =
                    _unavailableSinceUtc;

                _unavailableSinceUtc = null;
            }

            if (unavailableSinceUtc is null)
                return;

            var outageDuration =
                DateTimeOffset.UtcNow -
                unavailableSinceUtc.Value;

            _logger.LogInformation(
                "Parent database connection restored during " +
                "{Operation}. Live data resumed after " +
                "{OutageDurationSeconds:F1} seconds.",
                operation,
                outageDuration.TotalSeconds);
        }
    }
}