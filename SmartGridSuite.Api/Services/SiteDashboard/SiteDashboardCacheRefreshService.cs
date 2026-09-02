using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SmartGridSuite.Api.Services.SiteDashboard
{
    public sealed class SiteDashboardCacheRefreshResult
    {
        public DateTime StartedAtUtc { get; init; }

        public DateTime CompletedAtUtc { get; init; }

        public long SyncRunId { get; init; }

        public int AmsSiteCount { get; init; }

        public int DacsSiteCount { get; init; }

        public int IgsdSiteCount { get; init; }

        public int RxSiteCount { get; init; }

        public int TowerCount { get; init; }

        public int TowerSectorCount { get; init; }

        public int TotalSiteCount =>
            AmsSiteCount +
            DacsSiteCount +
            IgsdSiteCount +
            RxSiteCount;
    }

    /// <summary>
    /// Replaces the locally cached Parent DB snapshot in one MariaDB transaction.
    /// This service is intended for scheduled or manually requested refreshes only.
    /// It is not called during a technician's normal Site Dashboard lookup.
    /// </summary>
    public sealed class SiteDashboardCacheRefreshService
    {
        private const int InsertBatchSize = 500;

        private readonly SmartGridDbContext _db;
        private readonly ParentSyncService _parentSyncService;
        private readonly ILogger<SiteDashboardCacheRefreshService> _logger;

        private readonly ParentDatabaseHealthService _parentDatabaseHealth;

        public SiteDashboardCacheRefreshService(
            SmartGridDbContext db,
            ParentSyncService parentSyncService,
            ParentDatabaseHealthService parentDatabaseHealth,
            ILogger<SiteDashboardCacheRefreshService> logger)
        {
            _db = db;
            _parentSyncService = parentSyncService;
            _parentDatabaseHealth = parentDatabaseHealth;
            _logger = logger;
        }

        public async Task<SiteDashboardCacheRefreshResult> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            var startedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Starting full Parent DB Site Dashboard cache refresh.");

            /*
             * Read the complete Parent DB snapshot before opening the MariaDB
             * transaction. If the Parent DB query fails, the existing cache
             * remains untouched.
             */
            ParentCacheSnapshot snapshot;

            try
            {
                snapshot =
                    await _parentSyncService.GetCacheSnapshotAsync(
                        cancellationToken);

                _parentDatabaseHealth.RecordSuccess(
                    "Parent DB cache refresh");
            }
            catch (SqlException ex)
            {
                _parentDatabaseHealth.RecordFailure(
                    ex,
                    "Parent DB cache refresh");

                throw;
            }

            var syncedAtUtc = DateTime.UtcNow;

            var syncRunId =
                long.Parse(
                    syncedAtUtc.ToString(
                        "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture);

            var commonSites =
                BuildCommonSiteRows(snapshot);

            var towerSectorCount =
                snapshot.Towers.Sum(
                    tower => tower.Sectors.Count);

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                await ClearExistingCacheAsync(
                    cancellationToken);

                _db.ChangeTracker.Clear();

                await InsertCommonSiteCacheAsync(
                    commonSites,
                    syncedAtUtc,
                    syncRunId,
                    cancellationToken);

                await InsertAmsCacheAsync(
                    snapshot.AmsSites,
                    syncedAtUtc,
                    syncRunId,
                    cancellationToken);

                await InsertDacsCacheAsync(
                    snapshot.DacsSites,
                    syncedAtUtc,
                    syncRunId,
                    cancellationToken);

                await InsertIgsdCacheAsync(
                    snapshot.IgsdSites,
                    syncedAtUtc,
                    syncRunId,
                    cancellationToken);

                await InsertRxCacheAsync(
                    snapshot.RxSites,
                    syncedAtUtc,
                    syncRunId,
                    cancellationToken);

                await InsertTowerCacheAsync(
                    snapshot.Towers,
                    syncedAtUtc,
                    syncRunId,
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                throw;
            }

            var result =
                new SiteDashboardCacheRefreshResult
                {
                    StartedAtUtc = startedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    SyncRunId = syncRunId,

                    AmsSiteCount =
                        snapshot.AmsSites.Count,

                    DacsSiteCount =
                        snapshot.DacsSites.Count,

                    IgsdSiteCount =
                        snapshot.IgsdSites.Count,

                    RxSiteCount =
                        snapshot.RxSites.Count,

                    TowerCount =
                        snapshot.Towers.Count,

                    TowerSectorCount =
                        towerSectorCount
                };

            _logger.LogInformation(
                "Completed Parent DB cache refresh. " +
                "Sites: {SiteCount}; Towers: {TowerCount}; " +
                "Tower sectors: {TowerSectorCount}; Sync run: {SyncRunId}.",
                result.TotalSiteCount,
                result.TowerCount,
                result.TowerSectorCount,
                result.SyncRunId);

            return result;
        }

        private async Task ClearExistingCacheAsync(
            CancellationToken cancellationToken)
        {
            /*
             * DELETE is used instead of TRUNCATE so every operation remains
             * inside the same transaction. Other API requests continue seeing
             * the previously committed cache until this transaction commits.
             */
            string[] deleteStatements =
            {
                "DELETE FROM cache_tower_sector;",
                "DELETE FROM cache_tower;",
                "DELETE FROM cache_site_top;",
                "DELETE FROM cache_site_rx;",
                "DELETE FROM cache_site_igsd;",
                "DELETE FROM cache_site_dacs;",
                "DELETE FROM cache_site_pmr;",
                "DELETE FROM cache_site_lte;",
                "DELETE FROM cache_site_ams;",
                "DELETE FROM cache_site_gps;",
                "DELETE FROM cache_site_address;",
                "DELETE FROM cache_site;"
            };

            foreach (var sql in deleteStatements)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);
            }
        }

        private async Task InsertCommonSiteCacheAsync(
            IReadOnlyCollection<CommonSiteCacheRow> rows,
            DateTime syncedAtUtc,
            long syncRunId,
            CancellationToken cancellationToken)
        {
            var uniqueRows =
                rows
                    .Where(
                        row =>
                            !string.IsNullOrWhiteSpace(
                                row.SiteId))
                    .GroupBy(
                        row =>
                            NormalizeSiteId(row.SiteId),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(row => row.SiteId)
                    .ToList();

            var sites =
                uniqueRows.Select(
                    row =>
                        new CacheSiteEntity
                        {
                            SiteId =
                                NormalizeSiteId(row.SiteId),

                            SiteTypeCode =
                                NullIfBlank(row.SiteType)
                                ?? row.FallbackSiteType,

                            SiteStatus =
                                NullIfBlank(row.SiteStatus),

                            SiteConfigName =
                                NullIfBlank(row.SiteConfigName),

                            PrimaryCommType =
                                NullIfBlank(row.PrimaryCommType),

                            SecondaryCommType =
                                NullIfBlank(row.SecondaryCommType),

                            SiteConfigDescription =
                                NullIfBlank(
                                    row.SiteConfigDescription),

                            LastSyncedAt = syncedAtUtc,
                            SyncRunId = syncRunId,
                            IsActive = true,
                            CreatedAt = syncedAtUtc,
                            UpdatedAt = syncedAtUtc
                        })
                    .ToList();

            var addresses =
                uniqueRows
                    .Where(HasAddress)
                    .Select(
                        row =>
                            new CacheSiteAddressEntity
                            {
                                SiteId =
                                    NormalizeSiteId(row.SiteId),

                                StreetNumber =
                                    NullIfBlank(row.StreetNo),

                                StreetName =
                                    NullIfBlank(row.StreetName),

                                StreetAddress =
                                    BuildStreetAddress(
                                        row.StreetNo,
                                        row.StreetName),

                                City =
                                    NullIfBlank(row.City),

                                County =
                                    NullIfBlank(row.County),

                                StateCode =
                                    NullIfBlank(row.StateCode),

                                ZipCode =
                                    NullIfBlank(row.ZipCode),

                                LastSyncedAt = syncedAtUtc,
                                SyncRunId = syncRunId,
                                IsActive = true,
                                CreatedAt = syncedAtUtc,
                                UpdatedAt = syncedAtUtc
                            })
                    .ToList();

            var gpsRows =
                uniqueRows
                    .Where(
                        row =>
                            row.Latitude.HasValue ||
                            row.Longitude.HasValue)
                    .Select(
                        row =>
                            new CacheSiteGpsEntity
                            {
                                SiteId =
                                    NormalizeSiteId(row.SiteId),

                                Latitude = row.Latitude,
                                Longitude = row.Longitude,

                                LastSyncedAt = syncedAtUtc,
                                SyncRunId = syncRunId,
                                IsActive = true,
                                CreatedAt = syncedAtUtc,
                                UpdatedAt = syncedAtUtc
                            })
                    .ToList();

            var topRows =
                uniqueRows
                    .Where(HasTopData)
                    .Select(
                        row =>
                            new CacheSiteTopEntity
                            {
                                SiteId =
                                    NormalizeSiteId(row.SiteId),

                                TopName =
                                    NullIfBlank(row.TopName),

                                TopDescription =
                                    NullIfBlank(
                                        row.TopDescription),

                                TopSector =
                                    NullIfBlank(row.TopSector),

                                TopVip =
                                    NullIfBlank(row.TopVip),

                                TopIpA =
                                    NullIfBlank(row.TopIpA),

                                TopIpB =
                                    NullIfBlank(row.TopIpB),

                                LastSyncedAt = syncedAtUtc,
                                SyncRunId = syncRunId,
                                IsActive = true,
                                CreatedAt = syncedAtUtc,
                                UpdatedAt = syncedAtUtc
                            })
                    .ToList();

            await InsertInBatchesAsync(
                sites,
                cancellationToken);

            await InsertInBatchesAsync(
                addresses,
                cancellationToken);

            await InsertInBatchesAsync(
                gpsRows,
                cancellationToken);

            await InsertInBatchesAsync(
                topRows,
                cancellationToken);
        }

        private async Task InsertAmsCacheAsync(
            IReadOnlyCollection<AmsSiteDashboardRow> rows,
            DateTime syncedAtUtc,
            long syncRunId,
            CancellationToken cancellationToken)
        {
            var amsRows =
                rows.Select(
                    row =>
                        new CacheSiteAmsEntity
                        {
                            SiteId =
                                NormalizeSiteId(row.SiteId),

                            PrimaryCommsIdentifier =
                                NullIfBlank(
                                    row.PrimaryCommsIdentifier),

                            PrimaryCommsIp =
                                NullIfBlank(
                                    row.PrimaryCommsIp),

                            SecondaryLanIp =
                                NullIfBlank(
                                    row.SecondaryLanIp),

                            AntennaSerialNumber =
                                NullIfBlank(
                                    row.AntennaSerialNumber),

                            EnclosureSerialNumber =
                                NullIfBlank(
                                    row.EnclosureSerialNumber),

                            EnclosureModel =
                                NullIfBlank(
                                    row.EnclosureModel),

                            LastSyncedAt = syncedAtUtc,
                            SyncRunId = syncRunId,
                            IsActive = true,
                            CreatedAt = syncedAtUtc,
                            UpdatedAt = syncedAtUtc
                        })
                    .ToList();

            var lteRows =
                rows
                    .Where(
                        row =>
                            !string.IsNullOrWhiteSpace(
                                row.SecondarySimNumber) ||
                            !string.IsNullOrWhiteSpace(
                                row.SecondaryWanIp))
                    .Select(
                        row =>
                            new CacheSiteLteEntity
                            {
                                SiteId =
                                    NormalizeSiteId(row.SiteId),

                                SimNumber =
                                    NullIfBlank(
                                        row.SecondarySimNumber),

                                SecondaryWanIp =
                                    NullIfBlank(
                                        row.SecondaryWanIp),

                                LastSyncedAt = syncedAtUtc,
                                SyncRunId = syncRunId,
                                IsActive = true,
                                CreatedAt = syncedAtUtc,
                                UpdatedAt = syncedAtUtc
                            })
                    .ToList();

            var pmrRows =
                rows
                    .Where(
                        row =>
                            !string.IsNullOrWhiteSpace(
                                row.SecondaryCommsIdentifier) ||
                            !string.IsNullOrWhiteSpace(
                                row.SecondaryCommsUsername) ||
                            !string.IsNullOrWhiteSpace(
                                row.SecondaryCommsSsid) ||
                            !string.IsNullOrWhiteSpace(
                                row.SecondaryCommsPassword))
                    .Select(
                        row =>
                            new CacheSitePmrEntity
                            {
                                SiteId =
                                    NormalizeSiteId(row.SiteId),

                                SecondaryCommsIdentifier =
                                    NullIfBlank(
                                        row.SecondaryCommsIdentifier),

                                SecondaryCommsUsername =
                                    NullIfBlank(
                                        row.SecondaryCommsUsername),

                                SecondaryCommsSsid =
                                    NullIfBlank(
                                        row.SecondaryCommsSsid),

                                SecondaryCommsPassword =
                                    NullIfBlank(
                                        row.SecondaryCommsPassword),

                                LastSyncedAt = syncedAtUtc,
                                SyncRunId = syncRunId,
                                IsActive = true,
                                CreatedAt = syncedAtUtc,
                                UpdatedAt = syncedAtUtc
                            })
                    .ToList();

            await InsertInBatchesAsync(
                amsRows,
                cancellationToken);

            await InsertInBatchesAsync(
                lteRows,
                cancellationToken);

            await InsertInBatchesAsync(
                pmrRows,
                cancellationToken);
        }

        private async Task InsertDacsCacheAsync(
            IReadOnlyCollection<DacsSiteDashboardRow> rows,
            DateTime syncedAtUtc,
            long syncRunId,
            CancellationToken cancellationToken)
        {
            var entities =
                rows.Select(
                    row =>
                        new CacheSiteDacsEntity
                        {
                            SiteId =
                                NormalizeSiteId(row.SiteId),

                            PrimaryCommsIp =
                                NullIfBlank(
                                    row.PrimaryCommsIp),

                            TunnelIp =
                                NullIfBlank(row.TunnelIp),

                            RtuIp =
                                NullIfBlank(row.RtuIp),

                            LastSyncedAt = syncedAtUtc,
                            SyncRunId = syncRunId,
                            IsActive = true,
                            CreatedAt = syncedAtUtc,
                            UpdatedAt = syncedAtUtc
                        })
                    .ToList();

            await InsertInBatchesAsync(
                entities,
                cancellationToken);
        }

        private async Task InsertIgsdCacheAsync(
            IReadOnlyCollection<IgsdSiteDashboardRow> rows,
            DateTime syncedAtUtc,
            long syncRunId,
            CancellationToken cancellationToken)
        {
            var entities =
                rows.Select(
                    row =>
                        new CacheSiteIgsdEntity
                        {
                            SiteId =
                                NormalizeSiteId(row.SiteId),

                            PrimaryCommsIdentifier =
                                NullIfBlank(
                                    row.PrimaryCommsIdentifier),

                            PrimaryCommsIp =
                                NullIfBlank(
                                    row.PrimaryCommsIp),

                            PrimaryLanIp =
                                NullIfBlank(
                                    row.PrimaryLanIp),

                            PrimaryWanIp =
                                NullIfBlank(
                                    row.PrimaryWanIp),

                            PrimaryTunnelIp =
                                NullIfBlank(
                                    row.PrimaryTunnelIp),

                            PrimaryRtuIp =
                                NullIfBlank(
                                    row.PrimaryRtuIp),

                            SecondaryCommsIdentifier =
                                NullIfBlank(
                                    row.SecondaryCommsIdentifier),

                            SecondaryWanIp =
                                NullIfBlank(
                                    row.SecondaryWanIp),

                            SecondaryLanIp =
                                NullIfBlank(
                                    row.SecondaryLanIp),

                            SecondaryTunnelIp =
                                NullIfBlank(
                                    row.SecondaryTunnelIp),

                            SecondaryRtuIp =
                                NullIfBlank(
                                    row.SecondaryRtuIp),

                            AntennaSerialNumber =
                                NullIfBlank(
                                    row.AntennaSerialNumber),

                            EnclosureSerialNumber =
                                NullIfBlank(
                                    row.EnclosureSerialNumber),

                            EnclosureModel =
                                NullIfBlank(
                                    row.EnclosureModel),

                            CyberlockSerialNumber =
                                NullIfBlank(
                                    row.CyberlockSerialNumber),

                            TunnelPsk =
                                NullIfBlank(row.TunnelPsk),

                            LastSyncedAt = syncedAtUtc,
                            SyncRunId = syncRunId,
                            IsActive = true,
                            CreatedAt = syncedAtUtc,
                            UpdatedAt = syncedAtUtc
                        })
                    .ToList();

            await InsertInBatchesAsync(
                entities,
                cancellationToken);
        }

        private async Task InsertRxCacheAsync(
            IReadOnlyCollection<RxSiteDashboardRow> rows,
            DateTime syncedAtUtc,
            long syncRunId,
            CancellationToken cancellationToken)
        {
            var entities =
                rows.Select(
                    row =>
                        new CacheSiteRxEntity
                        {
                            SiteId =
                                NormalizeSiteId(row.SiteId),

                            MeterNumber =
                                NullIfBlank(row.MeterNumber),

                            MacAddress =
                                NullIfBlank(row.MacAddress),

                            PolePoint =
                                NullIfBlank(row.PolePoint),

                            TransformerGln =
                                NullIfBlank(
                                    row.TransformerGln),

                            LastSyncedAt = syncedAtUtc,
                            SyncRunId = syncRunId,
                            IsActive = true,
                            CreatedAt = syncedAtUtc,
                            UpdatedAt = syncedAtUtc
                        })
                    .ToList();

            await InsertInBatchesAsync(
                entities,
                cancellationToken);
        }

        private async Task InsertTowerCacheAsync(
            IReadOnlyCollection<TowerDashboardRow> rows,
            DateTime syncedAtUtc,
            long syncRunId,
            CancellationToken cancellationToken)
        {
            var towers =
                rows.Select(
                    row =>
                        new CacheTowerEntity
                        {
                            TopNameId = row.TopNameId,

                            TopName =
                                NullIfBlank(row.TopName),

                            TopType =
                                NullIfBlank(row.TopType),

                            TopDescription =
                                NullIfBlank(
                                    row.TopDescription),

                            IpAssignment =
                                NullIfBlank(
                                    row.IpAssignment),

                            SourceGpsId = row.GpsId,
                            SourceCnpAreaId = row.CnpAreaId,
                            CustomerOwned = row.CustomerOwned,

                            Note =
                                NullIfBlank(row.Note),

                            Latitude = row.Latitude,
                            Longitude = row.Longitude,

                            StreetNumber =
                                NullIfBlank(row.StreetNo),

                            StreetName =
                                NullIfBlank(row.StreetName),

                            StreetAddress =
                                BuildStreetAddress(
                                    row.StreetNo,
                                    row.StreetName),

                            City =
                                NullIfBlank(row.City),

                            County =
                                NullIfBlank(row.County),

                            StateCode =
                                NullIfBlank(row.StateCode),

                            ZipCode =
                                NullIfBlank(row.ZipCode),

                            FullAddress =
                                NullIfBlank(row.FullAddress),

                            HistorySiteId =
                                NullIfBlank(
                                    row.HistorySiteId),

                            LastSyncedAt = syncedAtUtc,
                            SyncRunId = syncRunId,
                            IsActive = true,
                            CreatedAt = syncedAtUtc,
                            UpdatedAt = syncedAtUtc
                        })
                    .ToList();

            var sectors =
                rows
                    .SelectMany(
                        tower =>
                            tower.Sectors.Select(
                                sector =>
                                    new CacheTowerSectorEntity
                                    {
                                        TopNameId =
                                            tower.TopNameId,

                                        TopSiteId =
                                            sector.TopSiteId,

                                        Sector =
                                            NullIfBlank(
                                                sector.Sector),

                                        TxDbm = sector.TxDbm,
                                        Downtilt =
                                            sector.Downtilt,

                                        ChannelNumber =
                                            sector.Channel,

                                        ChannelTxFrequency =
                                            sector.ChannelTxFrequency,

                                        ChannelRxFrequency =
                                            sector.ChannelRxFrequency,

                                        NetworkName =
                                            NullIfBlank(
                                                sector.Network),

                                        Vip =
                                            NullIfBlank(
                                                sector.Vip),

                                        IpA =
                                            NullIfBlank(
                                                sector.IPa),

                                        IpB =
                                            NullIfBlank(
                                                sector.IPb),

                                        Vlan =
                                            NullIfBlank(
                                                sector.Vlan),

                                        Bsid =
                                            NullIfBlank(
                                                sector.Bsid),

                                        AntennaSerialA =
                                            NullIfBlank(
                                                sector.AntennaSerialA),

                                        AntennaSerialB =
                                            NullIfBlank(
                                                sector.AntennaSerialB),

                                        Height = sector.Height,
                                        TestedHeight =
                                            sector.TestedHeight,

                                        Bearing = sector.Bearing,
                                        HighMount =
                                            sector.HighMount,

                                        LastSyncedAt =
                                            syncedAtUtc,

                                        SyncRunId = syncRunId,
                                        IsActive = true,
                                        CreatedAt = syncedAtUtc,
                                        UpdatedAt = syncedAtUtc
                                    }))
                    .ToList();

            await InsertInBatchesAsync(
                towers,
                cancellationToken);

            await InsertInBatchesAsync(
                sectors,
                cancellationToken);
        }

        private async Task InsertInBatchesAsync<TEntity>(
            IReadOnlyCollection<TEntity> entities,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            if (entities.Count == 0)
            {
                return;
            }

            foreach (var batch in entities.Chunk(InsertBatchSize))
            {
                await _db.Set<TEntity>()
                    .AddRangeAsync(
                        batch,
                        cancellationToken);

                await _db.SaveChangesAsync(
                    cancellationToken);

                _db.ChangeTracker.Clear();
            }
        }

        private static List<CommonSiteCacheRow> BuildCommonSiteRows(
            ParentCacheSnapshot snapshot)
        {
            var rows =
                new List<CommonSiteCacheRow>(
                    snapshot.AmsSites.Count +
                    snapshot.DacsSites.Count +
                    snapshot.IgsdSites.Count +
                    snapshot.RxSites.Count);

            rows.AddRange(
                snapshot.AmsSites.Select(
                    row =>
                        new CommonSiteCacheRow
                        {
                            SiteId = row.SiteId,
                            FallbackSiteType = "AMS",
                            SiteStatus = row.SiteStatus,
                            SiteType = row.SiteType,
                            SiteConfigName = row.SiteConfigName,
                            PrimaryCommType = row.PrimaryCommType,
                            SecondaryCommType =
                                row.SecondaryCommType,
                            SiteConfigDescription =
                                row.SiteConfigDescription,
                            TopName = row.TopName,
                            TopDescription = row.TopDescription,
                            TopSector = row.TopSector,
                            TopVip = row.TopVip,
                            TopIpA = row.TopIpA,
                            TopIpB = row.TopIpB,
                            StreetNo = row.StreetNo,
                            StreetName = row.StreetName,
                            City = row.City,
                            County = row.County,
                            StateCode = row.StateCode,
                            ZipCode = row.ZipCode,
                            Latitude = row.Latitude,
                            Longitude = row.Longitude
                        }));

            rows.AddRange(
                snapshot.DacsSites.Select(
                    row =>
                        new CommonSiteCacheRow
                        {
                            SiteId = row.SiteId,
                            FallbackSiteType = "DACS",
                            SiteStatus = row.SiteStatus,
                            SiteType = row.SiteType,
                            SiteConfigName = row.SiteConfigName,
                            PrimaryCommType = row.PrimaryCommType,
                            SecondaryCommType =
                                row.SecondaryCommType,
                            SiteConfigDescription =
                                row.SiteConfigDescription,
                            TopName = row.TopName,
                            TopDescription = row.TopDescription,
                            TopSector = row.TopSector,
                            TopVip = row.TopVip,
                            TopIpA = row.TopIpA,
                            TopIpB = row.TopIpB,
                            StreetNo = row.StreetNo,
                            StreetName = row.StreetName,
                            City = row.City,
                            County = row.County,
                            StateCode = row.StateCode,
                            ZipCode = row.ZipCode,
                            Latitude = row.Latitude,
                            Longitude = row.Longitude
                        }));

            rows.AddRange(
                snapshot.IgsdSites.Select(
                    row =>
                        new CommonSiteCacheRow
                        {
                            SiteId = row.SiteId,
                            FallbackSiteType = "IGSD",
                            SiteStatus = row.SiteStatus,
                            SiteType = row.SiteType,
                            SiteConfigName = row.SiteConfigName,
                            PrimaryCommType = row.PrimaryCommType,
                            SecondaryCommType =
                                row.SecondaryCommType,
                            SiteConfigDescription =
                                row.SiteConfigDescription,
                            TopName = row.TopName,
                            TopDescription = row.TopDescription,
                            TopSector = row.TopSector,
                            TopVip = row.TopVip,
                            TopIpA = row.TopIpA,
                            TopIpB = row.TopIpB,
                            StreetNo = row.StreetNo,
                            StreetName = row.StreetName,
                            City = row.City,
                            County = row.County,
                            StateCode = row.StateCode,
                            ZipCode = row.ZipCode,
                            Latitude = row.Latitude,
                            Longitude = row.Longitude
                        }));

            rows.AddRange(
                snapshot.RxSites.Select(
                    row =>
                        new CommonSiteCacheRow
                        {
                            SiteId = row.SiteId,
                            FallbackSiteType = "RE",
                            SiteStatus = row.SiteStatus,
                            SiteType = row.SiteType,
                            SiteConfigName = row.SiteConfigName,
                            SiteConfigDescription =
                                row.SiteConfigDescription,
                            StreetNo = row.StreetNo,
                            StreetName = row.StreetName,
                            City = row.City,
                            County = row.County,
                            StateCode = row.StateCode,
                            ZipCode = row.ZipCode,
                            Latitude = row.Latitude,
                            Longitude = row.Longitude
                        }));

            return rows;
        }

        private static bool HasAddress(
            CommonSiteCacheRow row)
        {
            return
                !string.IsNullOrWhiteSpace(row.StreetNo) ||
                !string.IsNullOrWhiteSpace(row.StreetName) ||
                !string.IsNullOrWhiteSpace(row.City) ||
                !string.IsNullOrWhiteSpace(row.County) ||
                !string.IsNullOrWhiteSpace(row.StateCode) ||
                !string.IsNullOrWhiteSpace(row.ZipCode);
        }

        private static bool HasTopData(
            CommonSiteCacheRow row)
        {
            return
                !string.IsNullOrWhiteSpace(row.TopName) ||
                !string.IsNullOrWhiteSpace(row.TopDescription) ||
                !string.IsNullOrWhiteSpace(row.TopSector) ||
                !string.IsNullOrWhiteSpace(row.TopVip) ||
                !string.IsNullOrWhiteSpace(row.TopIpA) ||
                !string.IsNullOrWhiteSpace(row.TopIpB);
        }

        private static string? BuildStreetAddress(
            string? streetNo,
            string? streetName)
        {
            var value =
                string.Join(
                    " ",
                    new[]
                    {
                        NullIfBlank(streetNo),
                        NullIfBlank(streetName)
                    }
                    .Where(
                        text =>
                            !string.IsNullOrWhiteSpace(text)));

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        private static string NormalizeSiteId(
            string? siteId)
        {
            return (siteId ?? "")
                .Trim()
                .ToUpperInvariant();
        }

        private static string? NullIfBlank(
            string? value)
        {
            var trimmed =
                (value ?? "").Trim();

            return string.IsNullOrWhiteSpace(trimmed)
                ? null
                : trimmed;
        }

        private sealed class CommonSiteCacheRow
        {
            public string SiteId { get; init; } = "";

            public string FallbackSiteType { get; init; } = "";

            public string? SiteStatus { get; init; }

            public string? SiteType { get; init; }

            public string? SiteConfigName { get; init; }

            public string? PrimaryCommType { get; init; }

            public string? SecondaryCommType { get; init; }

            public string? SiteConfigDescription { get; init; }

            public string? TopName { get; init; }

            public string? TopDescription { get; init; }

            public string? TopSector { get; init; }

            public string? TopVip { get; init; }

            public string? TopIpA { get; init; }

            public string? TopIpB { get; init; }

            public string? StreetNo { get; init; }

            public string? StreetName { get; init; }

            public string? City { get; init; }

            public string? County { get; init; }

            public string? StateCode { get; init; }

            public string? ZipCode { get; init; }

            public decimal? Latitude { get; init; }

            public decimal? Longitude { get; init; }
        }
    }
}