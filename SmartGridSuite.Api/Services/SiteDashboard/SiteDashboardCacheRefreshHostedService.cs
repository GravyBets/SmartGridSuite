using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;

namespace SmartGridSuite.Api.Services.SiteDashboard
{
    public sealed class SiteDashboardCacheRefreshHostedService
        : BackgroundService
    {
        /*
         * Retry times are relative to the normal scheduled run.
         *
         * Normal run:  2:00 AM
         * Retry #1:    2:15 AM
         * Retry #2:    2:45 AM
         * Retry #3:    4:00 AM
         */
        private static readonly TimeSpan[] RetryOffsets =
        {
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromHours(2)
        };

        private readonly IServiceScopeFactory _scopeFactory;

        private readonly
            IOptionsMonitor<SiteDashboardCacheRefreshOptions>
            _optionsMonitor;

        private readonly
            ILogger<SiteDashboardCacheRefreshHostedService>
            _logger;

        public SiteDashboardCacheRefreshHostedService(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<SiteDashboardCacheRefreshOptions>
                optionsMonitor,
            ILogger<SiteDashboardCacheRefreshHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var options =
                    _optionsMonitor.CurrentValue;

                if (!options.Enabled)
                {
                    _logger.LogInformation(
                        "The Site Dashboard cache refresh schedule is disabled.");

                    await DelaySafelyAsync(
                        TimeSpan.FromMinutes(15),
                        stoppingToken);

                    continue;
                }

                var now =
                    DateTimeOffset.Now;

                var nextRun =
                    CalculateNextRun(
                        now,
                        options.DayOfWeek,
                        options.TimeOfDay);

                var delay =
                    nextRun - now;

                _logger.LogInformation(
                    "The next Site Dashboard cache refresh is scheduled " +
                    "for {NextRunLocal}.",
                    nextRun);

                if (!await DelaySafelyAsync(
                        delay,
                        stoppingToken))
                {
                    break;
                }

                /*
                 * Run the normal scheduled refresh. If it fails, this method
                 * owns the same-day retry sequence before we calculate the
                 * next weekly run.
                 */
                if (!await RunScheduledRefreshWithRetriesAsync(
                        nextRun,
                        stoppingToken))
                {
                    break;
                }
            }
        }

        private async Task<bool>
            RunScheduledRefreshWithRetriesAsync(
                DateTimeOffset scheduledRun,
                CancellationToken stoppingToken)
        {
            var totalAttempts =
                RetryOffsets.Length + 1;

            for (var attemptIndex = 0;
                 attemptIndex < totalAttempts;
                 attemptIndex++)
            {
                /*
                 * attemptIndex 0 is the normal scheduled attempt.
                 * Later attempts use the configured offsets from the
                 * original scheduled run time.
                 */
                if (attemptIndex > 0)
                {
                    var retryNumber =
                        attemptIndex;

                    var retryAt =
                        scheduledRun +
                        RetryOffsets[attemptIndex - 1];

                    var retryDelay =
                        retryAt - DateTimeOffset.Now;

                    _logger.LogWarning(
                        "Site Dashboard cache refresh retry " +
                        "{RetryNumber} of {RetryCount} is scheduled " +
                        "for {RetryAtLocal}.",
                        retryNumber,
                        RetryOffsets.Length,
                        retryAt);

                    if (retryDelay > TimeSpan.Zero)
                    {
                        if (!await DelaySafelyAsync(
                                retryDelay,
                                stoppingToken))
                        {
                            return false;
                        }
                    }

                    if (stoppingToken.IsCancellationRequested)
                    {
                        return false;
                    }
                }

                try
                {
                    /*
                     * Create a fresh scope for every attempt.
                     *
                     * This gives every retry a new scoped DbContext and
                     * refresh service instead of reusing state from a failed
                     * attempt.
                     */
                    using var scope =
                        _scopeFactory.CreateScope();

                    var refreshService =
                        scope.ServiceProvider
                            .GetRequiredService<
                                SiteDashboardCacheRefreshService>();

                    if (attemptIndex == 0)
                    {
                        _logger.LogInformation(
                            "Starting the scheduled Site Dashboard " +
                            "cache refresh.");
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Starting Site Dashboard cache refresh " +
                            "retry {RetryNumber} of {RetryCount}.",
                            attemptIndex,
                            RetryOffsets.Length);
                    }

                    var result =
                        await refreshService.RefreshAsync(
                            stoppingToken);

                    if (attemptIndex == 0)
                    {
                        _logger.LogInformation(
                            "Scheduled Site Dashboard cache refresh " +
                            "completed. " +
                            "SyncRunId: {SyncRunId}; " +
                            "AMS: {AmsCount}; " +
                            "DACS: {DacsCount}; " +
                            "IGSD: {IgsdCount}; " +
                            "RX: {RxCount}; " +
                            "Towers: {TowerCount}; " +
                            "Tower sectors: {TowerSectorCount}; " +
                            "Total sites: {TotalSiteCount}.",
                            result.SyncRunId,
                            result.AmsSiteCount,
                            result.DacsSiteCount,
                            result.IgsdSiteCount,
                            result.RxSiteCount,
                            result.TowerCount,
                            result.TowerSectorCount,
                            result.TotalSiteCount);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Site Dashboard cache refresh retry " +
                            "{RetryNumber} completed successfully. " +
                            "SyncRunId: {SyncRunId}; " +
                            "AMS: {AmsCount}; " +
                            "DACS: {DacsCount}; " +
                            "IGSD: {IgsdCount}; " +
                            "RX: {RxCount}; " +
                            "Towers: {TowerCount}; " +
                            "Tower sectors: {TowerSectorCount}; " +
                            "Total sites: {TotalSiteCount}.",
                            attemptIndex,
                            result.SyncRunId,
                            result.AmsSiteCount,
                            result.DacsSiteCount,
                            result.IgsdSiteCount,
                            result.RxSiteCount,
                            result.TowerCount,
                            result.TowerSectorCount,
                            result.TotalSiteCount);
                    }

                    /*
                     * One successful attempt ends the retry sequence.
                     * The outer loop will then schedule next Sunday.
                     */
                    return true;
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    /*
                     * RefreshAsync preserves the prior cache when an
                     * attempt fails. Every retry therefore remains safe.
                     */
                    if (attemptIndex < RetryOffsets.Length)
                    {
                        _logger.LogError(
                            ex,
                            "Site Dashboard cache refresh attempt " +
                            "{AttemptNumber} of {TotalAttempts} failed. " +
                            "The previous cache remains available. " +
                            "Another retry will be attempted.",
                            attemptIndex + 1,
                            totalAttempts);
                    }
                    else
                    {
                        _logger.LogError(
                            ex,
                            "Site Dashboard cache refresh failed after " +
                            "{TotalAttempts} attempts. " +
                            "The previous cache remains available. " +
                            "The next regular refresh will use the " +
                            "configured weekly schedule.",
                            totalAttempts);
                    }
                }
            }

            /*
             * All attempts failed, but the host itself is still healthy.
             * Return true so the outer loop schedules the next normal
             * weekly run.
             */
            return true;
        }

        private static DateTimeOffset CalculateNextRun(
            DateTimeOffset now,
            DayOfWeek configuredDay,
            TimeSpan configuredTime)
        {
            var timeOfDay =
                configuredTime >= TimeSpan.Zero &&
                configuredTime < TimeSpan.FromDays(1)
                    ? configuredTime
                    : TimeSpan.FromHours(2);

            var localNow =
                now.ToLocalTime();

            var daysUntilRun =
                ((int)configuredDay -
                 (int)localNow.DayOfWeek +
                 7) % 7;

            var candidateLocal =
                DateTime.SpecifyKind(
                    localNow.Date
                        .AddDays(daysUntilRun)
                        .Add(timeOfDay),
                    DateTimeKind.Unspecified);

            if (candidateLocal <= localNow.DateTime)
            {
                candidateLocal =
                    candidateLocal.AddDays(7);
            }

            /*
             * A configured time such as 2:00 AM may not exist on the
             * daylight-saving transition date. Move forward until the
             * local time becomes valid.
             */
            while (TimeZoneInfo.Local.IsInvalidTime(candidateLocal))
            {
                candidateLocal =
                    candidateLocal.AddMinutes(30);
            }

            var utcOffset =
                TimeZoneInfo.Local.GetUtcOffset(candidateLocal);

            return new DateTimeOffset(
                candidateLocal,
                utcOffset);
        }

        private static async Task<bool> DelaySafelyAsync(
            TimeSpan delay,
            CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(
                    delay,
                    stoppingToken);

                return true;
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }
}