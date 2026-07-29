using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;

namespace SmartGridSuite.Api.Services.SiteDashboard
{
    public sealed class SiteDashboardCacheRefreshHostedService
        : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<SiteDashboardCacheRefreshOptions>
            _optionsMonitor;
        private readonly ILogger<SiteDashboardCacheRefreshHostedService>
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

                try
                {
                    using var scope =
                        _scopeFactory.CreateScope();

                    var refreshService =
                        scope.ServiceProvider
                            .GetRequiredService<
                                SiteDashboardCacheRefreshService>();

                    _logger.LogInformation(
                        "Starting the scheduled Site Dashboard cache refresh.");

                    var result =
                        await refreshService.RefreshAsync(
                            stoppingToken);

                    _logger.LogInformation(
                        "Scheduled Site Dashboard cache refresh completed. " +
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
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    /*
                     * RefreshAsync uses a MariaDB transaction, so a failed
                     * scheduled refresh leaves the previous cache intact.
                     */
                    _logger.LogError(
                        ex,
                        "The scheduled Site Dashboard cache refresh failed. " +
                        "The previous cache remains available.");
                }
            }
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