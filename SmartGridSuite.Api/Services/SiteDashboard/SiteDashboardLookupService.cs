using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.ParentSync.Models;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Net;
using System.Net.Sockets;

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
        private readonly ParentDatabaseHealthService _parentDatabaseHealth;
        private readonly ILogger<SiteDashboardLookupService> _logger;

        public SiteDashboardLookupService(
            ParentSyncService parentSyncService,
            SiteDashboardCacheService cacheService,
            ParentDatabaseHealthService parentDatabaseHealth,
            ILogger<SiteDashboardLookupService> logger)
        {
            _parentSyncService = parentSyncService;
            _cacheService = cacheService;
            _parentDatabaseHealth = parentDatabaseHealth;
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

                _parentDatabaseHealth.RecordSuccess("site dashboard lookup");

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
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "site dashboard lookup");

                return await GetCachedSiteAsync(
                    siteId,
                    cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "site dashboard lookup");

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

        public async Task<AssociatedSiteByIpLookupDto>FindAssociatedSiteAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            query = (query ?? string.Empty).Trim();

            var isIpv4 =
                query.Split('.').Length == 4 &&
                IPAddress.TryParse(
                    query,
                    out var parsedAddress) &&
                parsedAddress.AddressFamily ==
                    AddressFamily.InterNetwork;

            if (isIpv4)
            {
                return await FindAssociatedSiteByIpAsync(
                    query,
                    cancellationToken);
            }

            using var liveLookupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            liveLookupCancellation.CancelAfter(
                LiveLookupTimeout);

            try
            {
                var result =
                    await _parentSyncService
                        .FindAssociatedSiteBySerialAsync(
                            query,
                            liveLookupCancellation.Token);

                _parentDatabaseHealth.RecordSuccess(
                    "serial-number lookup");

                return result;
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested &&
                      liveLookupCancellation.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "serial-number lookup");

                return await _parentSyncService
                    .FindAssociatedSiteBySerialFromCacheAsync(
                        query,
                        cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "serial-number lookup");

                return await _parentSyncService
                    .FindAssociatedSiteBySerialFromCacheAsync(
                        query,
                        cancellationToken);
            }
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
                var result =
                    await _parentSyncService
                        .FindAssociatedSiteByIpAsync(
                            ip,
                            liveLookupCancellation.Token);

                _parentDatabaseHealth.RecordSuccess(
                    "reverse-IP lookup");

                return result;
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested &&
                      liveLookupCancellation.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "reverse-IP lookup");

                return await _parentSyncService
                    .FindAssociatedSiteByIpFromCacheAsync(
                        ip,
                        cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "reverse-IP lookup");

                return await _parentSyncService
                    .FindAssociatedSiteByIpFromCacheAsync(
                        ip,
                        cancellationToken);
            }
        }

        public async Task<SiteDashboardLookupResult> GetTowerAsync(
            int topNameId,
            CancellationToken cancellationToken = default)
        {
            using var liveLookupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            liveLookupCancellation.CancelAfter(LiveLookupTimeout);

            try
            {
                var liveDashboard =
                    await _parentSyncService.GetTowerDashboardAsync(
                        topNameId,
                        liveLookupCancellation.Token);

                _parentDatabaseHealth.RecordSuccess("tower dashboard lookup");

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
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "tower dashboard lookup");

                return await GetCachedTowerAsync(
                    topNameId,
                    cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "tower dashboard lookup");

                return await GetCachedTowerAsync(
                    topNameId,
                    cancellationToken);
            }
        }

        private async Task<SiteDashboardLookupResult> GetCachedTowerAsync(
            int topNameId,
            CancellationToken cancellationToken)
        {
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

        public async Task<TowerSearchLookupResult> SearchTowersAsync(
            string term,
            int take = 25,
            CancellationToken cancellationToken = default)
        {
            using var liveLookupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            liveLookupCancellation.CancelAfter(LiveLookupTimeout);

            try
            {
                var liveRows =
                    await _parentSyncService.SearchTowersAsync(
                        term,
                        take,
                        liveLookupCancellation.Token);

                _parentDatabaseHealth.RecordSuccess("tower search");

                if (liveRows.Count > 0)
                {
                    return new TowerSearchLookupResult
                    {
                        State = SiteDashboardLookupState.Live,
                        Rows = liveRows
                    };
                }

                _logger.LogInformation(
                    "Parent database tower search returned no results for " +
                    "{SearchTerm}. Attempting cached tower search.",
                    term);

                return await SearchCachedTowersAsync(
                    term,
                    take,
                    cancellationToken);
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested &&
                      liveLookupCancellation.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "tower search");

                return await SearchCachedTowersAsync(
                    term,
                    take,
                    cancellationToken);
            }
            catch (SqlException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "tower search");

                return await SearchCachedTowersAsync(
                    term,
                    take,
                    cancellationToken);
            }
        }

        private async Task<TowerSearchLookupResult> SearchCachedTowersAsync(
            string term,
            int take,
            CancellationToken cancellationToken)
        {
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