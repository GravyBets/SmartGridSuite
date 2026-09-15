using System.Diagnostics;
using Microsoft.Data.SqlClient;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Contracts.Administration;

namespace SmartGridSuite.Api.Services.SystemHealth
{
    public sealed class SystemHealthService
    {
        private static readonly TimeSpan CacheWarningAge = TimeSpan.FromDays(8);

        private readonly SmartGridDbContext _db;
        private readonly ApplicationRuntimeHealthService _runtime;
        private readonly ParentDatabaseHealthService _parentDatabaseHealth;
        private readonly ServerHealthProbeService _serverHealth;
        private readonly ILogger<SystemHealthService> _logger;

        public SystemHealthService(
            SmartGridDbContext db,
            ApplicationRuntimeHealthService runtime,
            ParentDatabaseHealthService parentDatabaseHealth,
            ServerHealthProbeService serverHealth,
            ILogger<SystemHealthService> logger)
        {
            _db = db;
            _runtime = runtime;
            _parentDatabaseHealth = parentDatabaseHealth;
            _serverHealth = serverHealth;
            _logger = logger;
        }

        public async Task<SystemHealthDto> GetAsync(CancellationToken cancellationToken = default)
        {
            var result = new SystemHealthDto
            {
                GeneratedAtUtc = DateTime.UtcNow,

                Application = new ApplicationHealthDto
                {
                    ApiVersion = _runtime.ApiVersion,
                    StartedAtUtc =
                        _runtime.StartedAtUtc.UtcDateTime,
                    UptimeSeconds =
                        Math.Max(
                            0,
                            (long)_runtime.Uptime.TotalSeconds)
                },

                ParentDatabase =
                    GetParentDatabaseHealth(),

                Backup =
                    _serverHealth.GetBackupHealth(),

                Storage =
                    _serverHealth.GetStorageHealth()
            };

            var databaseConnected =
                await CheckApplicationDatabaseAsync(
                    result.Application,
                    cancellationToken);

            result.ParentDatabaseCache =
                databaseConnected
                    ? await GetCacheHealthAsync(
                        cancellationToken)
                    : new ParentDatabaseCacheHealthDto
                    {
                        Status = "Unavailable",
                        Message =
                            "Cache health could not be checked " +
                            "because the SmartGridSuite database " +
                            "is unavailable."
                    };

            return result;
        }

        public async Task<ParentDatabaseTestResponse> TestParentDatabaseAsync(
            ParentDatabaseConnectionFactory factory, CancellationToken ct)
        {
            const string operation = "Administrator Parent DB test";
            var succeeded = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var connection = factory.CreateConnection();
                // Verify a new physical connection, not an old pooled session.
                var settings = new SqlConnectionStringBuilder(connection.ConnectionString)
                {
                    Pooling = false,
                    ConnectTimeout = 5
                };
                connection.ConnectionString = settings.ConnectionString;
                await connection.OpenAsync(timeout.Token);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = 5;
                await command.ExecuteScalarAsync(timeout.Token);
                _parentDatabaseHealth.RecordSuccess(operation);
                succeeded = true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(ex, operation);
            }

            // Both successful and failed live tests return a complete fresh snapshot.
            // The client must inspect Succeeded; HTTP 200 alone isn't test success.
            return new ParentDatabaseTestResponse
            {
                Succeeded = succeeded,
                Message = succeeded
                    ? "Parent DB connection test succeeded."
                    : "Parent DB connection test failed. Review the updated failure details.",
                Health = await GetAsync(ct)
            };
        }

        private async Task<bool> CheckApplicationDatabaseAsync(
            ApplicationHealthDto result,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var timeout =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                var connected =
                    await _db.Database.CanConnectAsync(
                        timeout.Token);

                stopwatch.Stop();

                result.ApplicationDatabaseConnected =
                    connected;

                result.ApplicationDatabaseResponseMilliseconds =
                    stopwatch.ElapsedMilliseconds;

                result.Status =
                    connected
                        ? "Healthy"
                        : "Critical";

                result.Message =
                    connected
                        ? "The SmartGridSuite API and application " +
                          "database are available."
                        : "The SmartGridSuite application database " +
                          "is unavailable.";

                return connected;
            }
            catch (Exception ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();

                result.ApplicationDatabaseConnected = false;

                result.ApplicationDatabaseResponseMilliseconds =
                    stopwatch.ElapsedMilliseconds;

                result.Status = "Critical";

                result.Message =
                    "The SmartGridSuite application database " +
                    "health check failed.";

                _logger.LogWarning(
                    ex,
                    "SmartGridSuite application database health " +
                    "check failed.");

                return false;
            }
        }

        private ParentDatabaseHealthDto GetParentDatabaseHealth()
        {
            var snapshot =
                _parentDatabaseHealth.GetSnapshot();

            var status =
                snapshot.IsUnavailable
                    ? "Using Cache"
                    : snapshot.LastSuccessfulConnectionUtc.HasValue
                        ? "Connected"
                        : "Unknown";

            return new ParentDatabaseHealthDto
            {
                Status = status,

                IsUsingCache =
                    snapshot.IsUnavailable,

                LastSuccessfulConnectionUtc =
                    snapshot.LastSuccessfulConnectionUtc
                        ?.UtcDateTime,

                LastFailureUtc =
                    snapshot.LastFailureUtc
                        ?.UtcDateTime,

                UnavailableSinceUtc =
                    snapshot.UnavailableSinceUtc
                        ?.UtcDateTime,

                LastFailureOperation =
                    snapshot.LastFailureOperation,

                LastFailureMessage =
                    snapshot.LastFailureMessage
            };
        }

        private async Task<ParentDatabaseCacheHealthDto>GetCacheHealthAsync(
                CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var siteCount =
                    await _db.CacheSites
                        .AsNoTracking()
                        .CountAsync(
                            x => x.IsActive,
                            timeout.Token);

                var towerCount =
                    await _db.CacheTowers
                        .AsNoTracking()
                        .CountAsync(
                            x => x.IsActive,
                            timeout.Token);

                var sectorCount =
                    await _db.CacheTowerSectors
                        .AsNoTracking()
                        .CountAsync(
                            x => x.IsActive,
                            timeout.Token);

                var siteMarker =
                    await _db.CacheSites
                        .AsNoTracking()
                        .OrderByDescending(
                            x => x.LastSyncedAt)
                        .Select(
                            x => new CacheRefreshMarker
                            {
                                LastSyncedAt =
                                    x.LastSyncedAt,

                                SyncRunId =
                                    x.SyncRunId
                            })
                        .FirstOrDefaultAsync(
                            timeout.Token);

                var towerMarker =
                    await _db.CacheTowers
                        .AsNoTracking()
                        .OrderByDescending(
                            x => x.LastSyncedAt)
                        .Select(
                            x => new CacheRefreshMarker
                            {
                                LastSyncedAt =
                                    x.LastSyncedAt,

                                SyncRunId =
                                    x.SyncRunId
                            })
                        .FirstOrDefaultAsync(
                            timeout.Token);

                var sectorMarker =
                    await _db.CacheTowerSectors
                        .AsNoTracking()
                        .OrderByDescending(
                            x => x.LastSyncedAt)
                        .Select(
                            x => new CacheRefreshMarker
                            {
                                LastSyncedAt =
                                    x.LastSyncedAt,

                                SyncRunId =
                                    x.SyncRunId
                            })
                        .FirstOrDefaultAsync(
                            timeout.Token);

                var markers =
                    new List<CacheRefreshMarker>();

                if (siteMarker is not null)
                    markers.Add(siteMarker);

                if (towerMarker is not null)
                    markers.Add(towerMarker);

                if (sectorMarker is not null)
                    markers.Add(sectorMarker);

                var latest =
                    markers
                        .OrderByDescending(
                            x => x.LastSyncedAt)
                        .FirstOrDefault();

                var result =
                    new ParentDatabaseCacheHealthDto
                    {
                        SiteCount = siteCount,
                        TowerCount = towerCount,
                        SectorCount = sectorCount
                    };

                if (latest is null)
                {
                    result.Status = "Warning";
                    result.Message =
                        "No Parent DB cache snapshot is available.";

                    return result;
                }

                var lastRefreshedUtc =
                    DateTime.SpecifyKind(
                        latest.LastSyncedAt,
                        DateTimeKind.Utc);

                var age =
                    DateTime.UtcNow -
                    lastRefreshedUtc;

                result.LastRefreshedUtc =
                    lastRefreshedUtc;

                result.AgeHours =
                    Math.Max(
                        0,
                        age.TotalHours);

                result.SyncRunId =
                    latest.SyncRunId?.ToString(
                        CultureInfo.InvariantCulture);

                if (age > CacheWarningAge)
                {
                    result.Status = "Warning";
                    result.Message =
                        "The Parent DB cache snapshot is more " +
                        "than eight days old.";
                }
                else
                {
                    result.Status = "Healthy";
                    result.Message =
                        "The Parent DB cache snapshot is current.";
                }

                return result;
            }
            catch (Exception ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent DB cache health check failed.");

                return new ParentDatabaseCacheHealthDto
                {
                    Status = "Unavailable",
                    Message =
                        "Parent DB cache health could not be read."
                };
            }
        }

        private sealed class CacheRefreshMarker
        {
            public DateTime LastSyncedAt { get; init; }

            public long? SyncRunId { get; init; }
        }
    }
}