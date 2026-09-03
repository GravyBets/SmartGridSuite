using Microsoft.Data.SqlClient;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed class ParentDatabaseHealthService
    {
        private readonly ILogger<ParentDatabaseHealthService> _logger;

        private readonly object _stateLock = new();

        private DateTimeOffset? _unavailableSinceUtc;
        private DateTimeOffset? _lastSuccessfulConnectionUtc;
        private DateTimeOffset? _lastFailureUtc;
        private string? _lastFailureOperation;
        private string? _lastFailureMessage;

        public ParentDatabaseHealthService(ILogger<ParentDatabaseHealthService> logger)
        {
            _logger = logger;
        }

        public void RecordFailure(Exception exception, string operation)
        {
            /*
             * A network interruption can leave unusable SQL Server
             * connections inside SqlClient's connection pool.
             *
             * Clearing the pools forces the next live lookup to create
             * a completely new physical connection.
             */
            SqlConnection.ClearAllPools();

            var failureUtc = DateTimeOffset.UtcNow;
            var becameUnavailable = false;

            lock (_stateLock)
            {
                _lastFailureUtc = failureUtc;
                _lastFailureOperation = operation;
                _lastFailureMessage = exception.Message;

                if (_unavailableSinceUtc is null)
                {
                    _unavailableSinceUtc = failureUtc;
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
            var successUtc = DateTimeOffset.UtcNow;
            DateTimeOffset? unavailableSinceUtc;

            lock (_stateLock)
            {
                _lastSuccessfulConnectionUtc = successUtc;

                unavailableSinceUtc =
                    _unavailableSinceUtc;

                _unavailableSinceUtc = null;
            }

            if (unavailableSinceUtc is null)
                return;

            var outageDuration =
                successUtc -
                unavailableSinceUtc.Value;

            _logger.LogInformation(
                "Parent database connection restored during " +
                "{Operation}. Live data resumed after " +
                "{OutageDurationSeconds:F1} seconds.",
                operation,
                outageDuration.TotalSeconds);
        }

        public ParentDatabaseHealthSnapshot GetSnapshot()
        {
            lock (_stateLock)
            {
                return new ParentDatabaseHealthSnapshot
                {
                    IsUnavailable =
                        _unavailableSinceUtc is not null,

                    UnavailableSinceUtc =
                        _unavailableSinceUtc,

                    LastSuccessfulConnectionUtc =
                        _lastSuccessfulConnectionUtc,

                    LastFailureUtc =
                        _lastFailureUtc,

                    LastFailureOperation =
                        _lastFailureOperation,

                    LastFailureMessage =
                        _lastFailureMessage
                };
            }
        }
    }

    public sealed class ParentDatabaseHealthSnapshot
    {
        public bool IsUnavailable { get; init; }

        public DateTimeOffset? UnavailableSinceUtc { get; init; }

        public DateTimeOffset? LastSuccessfulConnectionUtc { get; init; }

        public DateTimeOffset? LastFailureUtc { get; init; }

        public string? LastFailureOperation { get; init; }

        public string? LastFailureMessage { get; init; }
    }
}