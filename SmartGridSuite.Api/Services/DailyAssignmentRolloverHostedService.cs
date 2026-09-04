namespace SmartGridSuite.Api.Services
{
    public sealed class DailyAssignmentRolloverHostedService
        : BackgroundService
    {
        private static readonly TimeSpan CheckInterval =
            TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ILogger<
            DailyAssignmentRolloverHostedService> _logger;

        private DateTime? _completedDate;

        public DailyAssignmentRolloverHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<DailyAssignmentRolloverHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            /*
             * Check immediately on API startup. If the API was
             * offline at 5:00 AM, this performs the missed
             * rollover as soon as service is restored.
             */
            while (!stoppingToken.IsCancellationRequested)
            {
                await TryRunCurrentDayAsync(
                    stoppingToken);

                try
                {
                    await Task.Delay(
                        CheckInterval,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task TryRunCurrentDayAsync(
            CancellationToken ct)
        {
            var now = DateTime.Now;
            var workDate = now.Date;

            if (now.TimeOfDay <
                DailyAssignmentRolloverService
                    .ScheduledRunTime)
            {
                return;
            }

            if (_completedDate == workDate)
                return;

            try
            {
                using var scope =
                    _scopeFactory.CreateScope();

                /*
                 * Rollover uses the current truck roster and
                 * crew lead, so today's board must exist first.
                 */
                var boardInitialization =
                    scope.ServiceProvider
                        .GetRequiredService<
                            TruckBoardInitializationService>();

                await boardInitialization
                    .EnsureBoardInitializedAsync(
                        workDate,
                        ct);

                var rollover =
                    scope.ServiceProvider
                        .GetRequiredService<
                            DailyAssignmentRolloverService>();

                var result =
                    await rollover
                        .EnsureCurrentDayRolloverAsync(
                            workDate,
                            ct);

                if (result.EmailDeliveryPending)
                {
                    _logger.LogWarning(
                        "Scheduled rollover completed for " +
                        "{WorkDate}, but an email remains " +
                        "pending. It will retry in one minute.",
                        workDate);

                    return;
                }

                _completedDate = workDate;

                _logger.LogInformation(
                    "Scheduled Daily Assignment rollover " +
                    "completed for {WorkDate}. " +
                    "Carried={CarriedCount}, " +
                    "Withdrawn={WithdrawnCount}, " +
                    "ChangedTargets={ChangedTargetCount}.",
                    result.WorkDate,
                    result.CarriedCount,
                    result.WithdrawnCount,
                    result.ChangedTargets.Count);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                /*
                 * Do not set _completedDate. The scheduler will
                 * retry one minute later.
                 */
                _logger.LogError(
                    ex,
                    "Scheduled Daily Assignment rollover " +
                    "failed for {WorkDate}. It will retry.",
                    workDate);
            }
        }
    }
}