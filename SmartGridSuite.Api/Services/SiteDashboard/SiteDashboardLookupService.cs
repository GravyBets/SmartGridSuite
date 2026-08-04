using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.ParentSync.Models;
using SmartGridSuite.Contracts.SiteDashboard;

namespace SmartGridSuite.Api.Services.SiteDashboard
{
    public enum SiteDashboardLookupState
    {
        Live,
        Cached,
        NotFound,
        ParentDatabaseUnavailable
    }

    public sealed class SiteDashboardLookupResult
    {
        public SiteDashboardLookupState State { get; init; }

        public SiteDashboardResponse? Dashboard { get; init; }
    }

    public sealed class TowerSearchLookupResult
    {
        public SiteDashboardLookupState State { get; init; }

        public IReadOnlyList<TowerSearchRow> Rows { get; init; } =
            Array.Empty<TowerSearchRow>();
    }

    public sealed class SiteDashboardLookupService
    {
        private static readonly TimeSpan LiveLookupTimeout =
            TimeSpan.FromSeconds(8);

        private readonly ParentSyncService _parentSyncService;
        private readonly SiteDashboardCacheService _cacheService;
        private readonly ILogger<SiteDashboardLookupService> _logger;

        public SiteDashboardLookupService(
            ParentSyncService parentSyncService,
            SiteDashboardCacheService cacheService,
            ILogger<SiteDashboardLookupService> logger)
        {
            _parentSyncService = parentSyncService;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<SiteDashboardLookupResult> GetAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            using var liveLookupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            liveLookupCancellation.CancelAfter(LiveLookupTimeout);

            try
            {
                SiteDashboardResponse? liveDashboard =
                    await _parentSyncService.GetSiteDashboardAsync(
                        siteId,
                        liveLookupCancellation.Token);

                if (liveDashboard is null)
                {
                    return new SiteDashboardLookupResult
                    {
                        State = SiteDashboardLookupState.NotFound
                    };
                }

                return new SiteDashboardLookupResult
                {
                    State = SiteDashboardLookupState.Live,
                    Dashboard = liveDashboard
                };
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested &&
                      liveLookupCancellation.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent database lookup timed out after {TimeoutSeconds} " +
                    "seconds for site {SiteId}. Attempting to use cached " +
                    "site information.",
                    LiveLookupTimeout.TotalSeconds,
                    siteId);

                return await GetCachedSiteAsync(
                    siteId,
                    cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent database lookup failed for site {SiteId}. " +
                    "Attempting to use cached site information.",
                    siteId);

                return await GetCachedSiteAsync(
                    siteId,
                    cancellationToken);
            }
        }

        private async Task<SiteDashboardLookupResult> GetCachedSiteAsync(
            string siteId, CancellationToken cancellationToken)
        {
            SiteDashboardResponse? cachedDashboard =
                await _cacheService.GetDashboardAsync(
                    siteId,
                    cancellationToken);

            if (cachedDashboard is not null)
            {
                return new SiteDashboardLookupResult
                {
                    State = SiteDashboardLookupState.Cached,
                    Dashboard = cachedDashboard
                };
            }

            return new SiteDashboardLookupResult
            {
                State =
                    SiteDashboardLookupState
                        .ParentDatabaseUnavailable
            };
        }

        public async Task<AssociatedSiteByIpLookupDto> FindAssociatedSiteByIpAsync(
        string ip,
        CancellationToken cancellationToken = default)
        {
            using var liveLookupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            liveLookupCancellation.CancelAfter(
                LiveLookupTimeout);

            try
            {
                return await _parentSyncService
                    .FindAssociatedSiteByIpAsync(
                        ip,
                        liveLookupCancellation.Token);
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested &&
                      liveLookupCancellation.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent database reverse-IP lookup timed out after " +
                    "{TimeoutSeconds} seconds for IP {IpAddress}. " +
                    "Attempting cached site lookup.",
                    LiveLookupTimeout.TotalSeconds,
                    ip);

                return await _parentSyncService
                    .FindAssociatedSiteByIpFromCacheAsync(
                        ip,
                        cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent database reverse-IP lookup failed for " +
                    "{IpAddress}. Attempting cached site lookup.",
                    ip);

                return await _parentSyncService
                    .FindAssociatedSiteByIpFromCacheAsync(
                        ip,
                        cancellationToken);
            }
        }

        public async Task<SiteDashboardLookupResult> GetTowerAsync(int topNameId, CancellationToken cancellationToken = default)
        {
            try
            {
                var liveDashboard =
                    await _parentSyncService.GetTowerDashboardAsync(
                        topNameId,
                        cancellationToken);

                if (liveDashboard is null)
                {
                    return new SiteDashboardLookupResult
                    {
                        State = SiteDashboardLookupState.NotFound
                    };
                }

                return new SiteDashboardLookupResult
                {
                    State = SiteDashboardLookupState.Live,
                    Dashboard = liveDashboard
                };
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent database lookup failed for tower {TopNameId}. " +
                    "Attempting to use cached tower information.",
                    topNameId);

                var cachedDashboard =
                    await _cacheService.GetTowerDashboardAsync(
                        topNameId,
                        cancellationToken);

                if (cachedDashboard is not null)
                {
                    return new SiteDashboardLookupResult
                    {
                        State = SiteDashboardLookupState.Cached,
                        Dashboard = cachedDashboard
                    };
                }

                return new SiteDashboardLookupResult
                {
                    State =
                        SiteDashboardLookupState
                            .ParentDatabaseUnavailable
                };
            }
        }

        public async Task<TowerSearchLookupResult> SearchTowersAsync(string term, int take = 25, CancellationToken cancellationToken = default)
        {
            try
            {
                var liveRows =
                    await _parentSyncService.SearchTowersAsync(
                        term,
                        take,
                        cancellationToken);

                return new TowerSearchLookupResult
                {
                    State = SiteDashboardLookupState.Live,
                    Rows = liveRows
                };
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Parent database tower search failed for {SearchTerm}. " +
                    "Attempting to use cached tower information.",
                    term);

                var cachedRows =
                    await _cacheService.SearchTowersAsync(
                        term,
                        take,
                        cancellationToken);

                return new TowerSearchLookupResult
                {
                    State = SiteDashboardLookupState.Cached,
                    Rows = cachedRows
                };
            }
        }
    }
}