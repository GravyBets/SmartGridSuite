using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services.ParentSync.Models;
using SmartGridSuite.Contracts.SiteDashboard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Api.Services.SiteDashboard
{
    /// <summary>
    /// Represents the locally cached Parent DB information for one site.
    /// </summary>
    public sealed class CachedSiteSnapshot
    {
        public required CacheSiteEntity Site { get; init; }

        public CacheSiteAddressEntity? Address { get; init; }

        public CacheSiteGpsEntity? Gps { get; init; }

        public CacheSiteAmsEntity? Ams { get; init; }

        public CacheSiteLteEntity? Lte { get; init; }

        public CacheSitePmrEntity? Pmr { get; init; }

        public CacheSiteTopEntity? Top { get; init; }

        public CacheSiteDacsEntity? Dacs { get; init; }

        public CacheSiteIgsdEntity? Igsd { get; init; }

        public CacheSiteRxEntity? Rx { get; init; }
    }

    /// <summary>
    /// Reads last-known Parent DB dashboard information from smartgrid_test.
    /// </summary>
    public sealed class SiteDashboardCacheService
    {
        private readonly SmartGridDbContext _db;

        public SiteDashboardCacheService(SmartGridDbContext db)
        {
            _db = db;
        }

        public async Task<CachedSiteSnapshot?> GetAsync(
            string siteId,
            CancellationToken cancellationToken = default)
        {
            var normalizedSiteId =
                NormalizeSiteId(siteId);

            if (string.IsNullOrWhiteSpace(normalizedSiteId))
            {
                return null;
            }

            foreach (var candidateSiteId
                     in GetLookupSiteIdCandidates(normalizedSiteId))
            {
                var site =
                    await _db.CacheSites
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == candidateSiteId &&
                                x.IsActive,
                            cancellationToken);

                if (site is null)
                {
                    continue;
                }

                /*
                 * These queries intentionally run one at a time.
                 * EF Core does not support multiple simultaneous
                 * operations on the same DbContext instance.
                 */
                var address =
                    await _db.CacheSiteAddresses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var gps =
                    await _db.CacheSiteGps
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var ams =
                    await _db.CacheSiteAms
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var lte =
                    await _db.CacheSiteLte
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var pmr =
                    await _db.CacheSitePmr
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var top =
                    await _db.CacheSiteTop
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var dacs =
                    await _db.CacheSiteDacs
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var igsd =
                    await _db.CacheSiteIgsd
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                var rx =
                    await _db.CacheSiteRx
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.SiteId == site.SiteId &&
                                x.IsActive,
                            cancellationToken);

                return new CachedSiteSnapshot
                {
                    Site = site,
                    Address = address,
                    Gps = gps,
                    Ams = ams,
                    Lte = lte,
                    Pmr = pmr,
                    Top = top,
                    Dacs = dacs,
                    Igsd = igsd,
                    Rx = rx
                };
            }

            return null;
        }

        public async Task<SiteDashboardResponse?> GetDashboardAsync(
            string siteId,
            CancellationToken cancellationToken = default)
        {
            var snapshot =
                await GetAsync(
                    siteId,
                    cancellationToken);

            if (snapshot is null)
            {
                return null;
            }

            var dashboardKind =
                ResolveDashboardKind(snapshot);

            return dashboardKind switch
            {
                SiteDashboardKinds.AmsMr =>
                    BuildAmsDashboard(snapshot),

                SiteDashboardKinds.Dacs =>
                    BuildDacsDashboard(snapshot),

                SiteDashboardKinds.Igsd =>
                    BuildIgsdDashboard(snapshot),

                SiteDashboardKinds.Rx =>
                    BuildRxDashboard(snapshot),

                _ => null
            };
        }

        public async Task<SiteDashboardResponse?> GetTowerDashboardAsync(
            int topNameId,
            CancellationToken cancellationToken = default)
        {
            if (topNameId <= 0)
            {
                return null;
            }

            var tower =
                await _db.CacheTowers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.TopNameId == topNameId &&
                            x.IsActive,
                        cancellationToken);

            if (tower is null)
            {
                return null;
            }

            var sectors =
                await _db.CacheTowerSectors
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.TopNameId == topNameId &&
                            x.IsActive)
                    .OrderBy(x => x.Sector)
                    .ThenBy(x => x.TopSiteId)
                    .ToListAsync(cancellationToken);

            var row =
                new TowerDashboardRow
                {
                    TopNameId = tower.TopNameId,
                    TopName = tower.TopName,
                    TopType = tower.TopType,
                    TopDescription = tower.TopDescription,
                    IpAssignment = tower.IpAssignment,
                    GpsId = tower.SourceGpsId,
                    CnpAreaId = tower.SourceCnpAreaId,
                    CustomerOwned = tower.CustomerOwned,
                    Note = tower.Note,

                    Latitude = tower.Latitude,
                    Longitude = tower.Longitude,

                    StreetNo = tower.StreetNumber,
                    StreetName = tower.StreetName,
                    City = tower.City,
                    County = tower.County,
                    StateCode = tower.StateCode,
                    ZipCode = tower.ZipCode,
                    FullAddress = tower.FullAddress,

                    HistorySiteId = tower.HistorySiteId
                };

            foreach (var sector in sectors)
            {
                row.Sectors.Add(
                    new TowerSectorRow
                    {
                        TopSiteId = sector.TopSiteId,
                        Sector = sector.Sector,

                        TxDbm = sector.TxDbm,
                        Downtilt = sector.Downtilt,

                        Channel = sector.ChannelNumber,
                        ChannelTxFrequency =
                            sector.ChannelTxFrequency,
                        ChannelRxFrequency =
                            sector.ChannelRxFrequency,

                        Network = sector.NetworkName,

                        Vip = sector.Vip,
                        IPa = sector.IpA,
                        IPb = sector.IpB,
                        Vlan = sector.Vlan,

                        Bsid = sector.Bsid,

                        AntennaSerialA =
                            sector.AntennaSerialA,
                        AntennaSerialB =
                            sector.AntennaSerialB,

                        Height = sector.Height,
                        TestedHeight = sector.TestedHeight,
                        Bearing = sector.Bearing,
                        HighMount = sector.HighMount
                    });
            }

            var syntheticSiteId =
                $"TOWER:{tower.TopNameId}";

            return CreateCachedResponse(
                syntheticSiteId,
                SiteDashboardKinds.Tower,
                new SiteDashboardRouteInfo
                {
                    SiteId = syntheticSiteId,
                    SiteType = "Tower",
                    SiteConfigName = "Tower",
                    SiteConfigDescription =
                        "Tower dashboard"
                },
                row,
                tower.LastSyncedAt);
        }

        public async Task<List<TowerSearchRow>> SearchTowersAsync(
            string term,
            int take = 25,
            CancellationToken cancellationToken = default)
        {
            term = (term ?? "").Trim();

            if (string.IsNullOrWhiteSpace(term))
            {
                return new List<TowerSearchRow>();
            }

            if (take <= 0)
            {
                take = 25;
            }

            if (take > 100)
            {
                take = 100;
            }

            var normalizedTerm =
                term.ToUpperInvariant();

            var towers =
                await _db.CacheTowers
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.IsActive &&
                            (
                                (x.TopName != null &&
                                 x.TopName.ToUpper()
                                     .Contains(normalizedTerm)) ||
                                (x.TopDescription != null &&
                                 x.TopDescription.ToUpper()
                                     .Contains(normalizedTerm))
                            ))
                    .OrderBy(x => x.TopName)
                    .Take(take)
                    .ToListAsync(cancellationToken);

            var topNameIds =
                towers
                    .Select(x => x.TopNameId)
                    .ToList();

            var sectorCounts =
                await _db.CacheTowerSectors
                    .AsNoTracking()
                    .Where(
                        x =>
                            x.IsActive &&
                            topNameIds.Contains(x.TopNameId))
                    .GroupBy(x => x.TopNameId)
                    .Select(
                        group =>
                            new
                            {
                                TopNameId = group.Key,
                                SectorCount = group.Count()
                            })
                    .ToDictionaryAsync(
                        x => x.TopNameId,
                        x => x.SectorCount,
                        cancellationToken);

            return towers
                .Select(
                    tower =>
                        new TowerSearchRow
                        {
                            TopNameId = tower.TopNameId,
                            TopName = tower.TopName,
                            TopType = tower.TopType,
                            TopDescription =
                                tower.TopDescription,
                            SectorCount =
                                sectorCounts.TryGetValue(
                                    tower.TopNameId,
                                    out var count)
                                    ? count
                                    : 0
                        })
                .ToList();
        }

        private static string? ResolveDashboardKind(
            CachedSiteSnapshot snapshot)
        {
            var siteType =
                (snapshot.Site.SiteTypeCode ?? "")
                .Trim()
                .ToUpperInvariant();

            var siteId =
                snapshot.Site.SiteId
                    .Trim()
                    .ToUpperInvariant();

            if (snapshot.Ams is not null ||
                siteType.Contains("AMS") ||
                siteId.EndsWith(
                    "MR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.AmsMr;
            }

            if (snapshot.Dacs is not null ||
                siteType.Contains("DACS"))
            {
                return SiteDashboardKinds.Dacs;
            }

            if (snapshot.Rx is not null ||
                siteId.StartsWith(
                    "RX",
                    StringComparison.OrdinalIgnoreCase) ||
                siteId.StartsWith(
                    "RE",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.Rx;
            }

            if (snapshot.Igsd is not null ||
                siteType.Contains("IG") ||
                siteId.StartsWith(
                    "G",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.Igsd;
            }

            return null;
        }

        private static SiteDashboardResponse BuildAmsDashboard(
            CachedSiteSnapshot snapshot)
        {
            var site = snapshot.Site;
            var address = snapshot.Address;
            var gps = snapshot.Gps;
            var ams = snapshot.Ams;
            var lte = snapshot.Lte;
            var pmr = snapshot.Pmr;
            var top = snapshot.Top;

            var dashboard =
                new AmsSiteDashboardRow
                {
                    SiteId = site.SiteId,
                    SiteStatus = site.SiteStatus,
                    SiteType = site.SiteTypeCode,
                    SiteConfigName = site.SiteConfigName,
                    PrimaryCommType = site.PrimaryCommType,
                    SecondaryCommType =
                        site.SecondaryCommType,
                    SiteConfigDescription =
                        site.SiteConfigDescription,

                    PrimaryCommsIdentifier =
                        ams?.PrimaryCommsIdentifier,

                    PrimaryCommsIp =
                        ams?.PrimaryCommsIp,

                    SecondaryLanIp =
                        ams?.SecondaryLanIp,

                    SecondaryWanIp =
                        lte?.SecondaryWanIp
                        ?? lte?.SecondaryWanIp2,

                    SecondaryCommsIdentifier =
                        pmr?.SecondaryCommsIdentifier,

                    SecondaryCommsUsername =
                        pmr?.SecondaryCommsUsername,

                    SecondaryCommsSsid =
                        pmr?.SecondaryCommsSsid,

                    SecondaryCommsPassword =
                        pmr?.SecondaryCommsPassword,

                    SecondarySimNumber =
                        lte?.SimNumber,

                    AntennaSerialNumber =
                        ams?.AntennaSerialNumber,

                    EnclosureSerialNumber =
                        ams?.EnclosureSerialNumber,

                    EnclosureModel =
                        ams?.EnclosureModel,

                    TopName = top?.TopName,
                    TopDescription = top?.TopDescription,
                    TopSector = top?.TopSector,
                    TopVip = top?.TopVip,
                    TopIpA = top?.TopIpA,
                    TopIpB = top?.TopIpB,

                    StreetNo = address?.StreetNumber,
                    StreetName = BuildStreetName(address),
                    City = address?.City,
                    County = address?.County,
                    StateCode = address?.StateCode,
                    ZipCode = address?.ZipCode,

                    Latitude = gps?.Latitude,
                    Longitude = gps?.Longitude
                };

            return CreateCachedResponse(
                site.SiteId,
                SiteDashboardKinds.AmsMr,
                BuildRoute(site),
                dashboard,
                site.LastSyncedAt);
        }

        private static SiteDashboardResponse BuildDacsDashboard(CachedSiteSnapshot snapshot)
        {
            var site = snapshot.Site;
            var address = snapshot.Address;
            var gps = snapshot.Gps;
            var dacs = snapshot.Dacs;
            var top = snapshot.Top;

            var dashboard =
                new DacsSiteDashboardRow
                {
                    SiteId = site.SiteId,
                    SiteStatus = site.SiteStatus,
                    SiteType = site.SiteTypeCode,
                    SiteConfigName = site.SiteConfigName,
                    PrimaryCommType = site.PrimaryCommType,
                    SecondaryCommType =
                        site.SecondaryCommType,
                    SiteConfigDescription =
                        site.SiteConfigDescription,

                    PrimaryCommsIp =
                        dacs?.PrimaryCommsIp,
                    TunnelIp = dacs?.TunnelIp,
                    RtuIp = dacs?.RtuIp,

                    TopName = top?.TopName,
                    TopDescription = top?.TopDescription,
                    TopSector = top?.TopSector,
                    TopVip = top?.TopVip,
                    TopIpA = top?.TopIpA,
                    TopIpB = top?.TopIpB,

                    StreetNo = address?.StreetNumber,
                    StreetName = BuildStreetName(address),
                    City = address?.City,
                    County = address?.County,
                    StateCode = address?.StateCode,
                    ZipCode = address?.ZipCode,

                    Latitude = gps?.Latitude,
                    Longitude = gps?.Longitude
                };

            return CreateCachedResponse(
                site.SiteId,
                SiteDashboardKinds.Dacs,
                BuildRoute(site),
                dashboard,
                site.LastSyncedAt);
        }

        private static SiteDashboardResponse BuildIgsdDashboard(CachedSiteSnapshot snapshot)
        {
            var site = snapshot.Site;
            var address = snapshot.Address;
            var gps = snapshot.Gps;
            var igsd = snapshot.Igsd;
            var top = snapshot.Top;

            var dashboard =
                new IgsdSiteDashboardRow
                {
                    SiteId = site.SiteId,
                    SiteStatus = site.SiteStatus,
                    SiteType = site.SiteTypeCode,
                    SiteConfigName = site.SiteConfigName,
                    PrimaryCommType = site.PrimaryCommType,
                    SecondaryCommType =
                        site.SecondaryCommType,
                    SiteConfigDescription =
                        site.SiteConfigDescription,

                    PrimaryCommsIdentifier =
                        igsd?.PrimaryCommsIdentifier,
                    PrimaryCommsIp =
                        igsd?.PrimaryCommsIp,
                    PrimaryLanIp =
                        igsd?.PrimaryLanIp,
                    PrimaryWanIp =
                        igsd?.PrimaryWanIp,
                    PrimaryTunnelIp =
                        igsd?.PrimaryTunnelIp,
                    PrimaryRtuIp =
                        igsd?.PrimaryRtuIp,

                    SecondaryCommsIdentifier =
                        igsd?.SecondaryCommsIdentifier,
                    SecondaryWanIp =
                        igsd?.SecondaryWanIp,
                    SecondaryLanIp =
                        igsd?.SecondaryLanIp,
                    SecondaryTunnelIp =
                        igsd?.SecondaryTunnelIp,
                    SecondaryRtuIp =
                        igsd?.SecondaryRtuIp,

                    AntennaSerialNumber =
                        igsd?.AntennaSerialNumber,
                    EnclosureSerialNumber =
                        igsd?.EnclosureSerialNumber,
                    EnclosureModel =
                        igsd?.EnclosureModel,
                    CyberlockSerialNumber =
                        igsd?.CyberlockSerialNumber,
                    TunnelPsk =
                        igsd?.TunnelPsk,

                    TopName = top?.TopName,
                    TopDescription = top?.TopDescription,
                    TopSector = top?.TopSector,
                    TopVip = top?.TopVip,
                    TopIpA = top?.TopIpA,
                    TopIpB = top?.TopIpB,

                    StreetNo = address?.StreetNumber,
                    StreetName = BuildStreetName(address),
                    City = address?.City,
                    County = address?.County,
                    StateCode = address?.StateCode,
                    ZipCode = address?.ZipCode,

                    Latitude = gps?.Latitude,
                    Longitude = gps?.Longitude
                };

            return CreateCachedResponse(
                site.SiteId,
                SiteDashboardKinds.Igsd,
                BuildRoute(site),
                dashboard,
                site.LastSyncedAt);
        }

        private static SiteDashboardResponse BuildRxDashboard(CachedSiteSnapshot snapshot)
        {
            var site = snapshot.Site;
            var address = snapshot.Address;
            var gps = snapshot.Gps;
            var rx = snapshot.Rx;

            var dashboard =
                new RxSiteDashboardRow
                {
                    SiteId = site.SiteId,
                    SiteStatus = site.SiteStatus,
                    SiteType = site.SiteTypeCode,
                    SiteConfigName = site.SiteConfigName,
                    SiteConfigDescription =
                        site.SiteConfigDescription,

                    MeterNumber = rx?.MeterNumber,
                    MacAddress = rx?.MacAddress,
                    PolePoint = rx?.PolePoint,
                    TransformerGln =
                        rx?.TransformerGln,

                    StreetNo = address?.StreetNumber,
                    StreetName = BuildStreetName(address),
                    City = address?.City,
                    County = address?.County,
                    StateCode = address?.StateCode,
                    ZipCode = address?.ZipCode,

                    Latitude = gps?.Latitude,
                    Longitude = gps?.Longitude
                };

            return CreateCachedResponse(
                site.SiteId,
                SiteDashboardKinds.Rx,
                BuildRoute(site),
                dashboard,
                site.LastSyncedAt);
        }

        private static SiteDashboardRouteInfo BuildRoute(
            CacheSiteEntity site)
        {
            return new SiteDashboardRouteInfo
            {
                SiteId = site.SiteId,
                SiteTypeId = site.SourceSiteTypeId,
                SiteType = site.SiteTypeCode,
                ConfigId = site.SourceConfigId,
                SiteConfigName = site.SiteConfigName,
                PrimaryCommType = site.PrimaryCommType,
                SecondaryCommType =
                    site.SecondaryCommType,
                SiteConfigDescription =
                    site.SiteConfigDescription
            };
        }

        private static SiteDashboardResponse CreateCachedResponse(
            string siteId,
            string dashboardKind,
            SiteDashboardRouteInfo route,
            object data,
            DateTime cachedAt)
        {
            return new SiteDashboardResponse
            {
                SiteId = siteId,
                DashboardKind = dashboardKind,
                Route = route,
                Data = data,
                IsCached = true,
                CachedAtUtc =
                    ConvertCacheTimeToUtc(cachedAt),
                DataWarning =
                    "The Parent database is unavailable. " +
                    "Displaying the last cached dashboard information. " +
                    "Some site details may have changed."
            };
        }

        private static string? BuildStreetName(CacheSiteAddressEntity? address)
        {
            if (address is null)
            {
                return null;
            }

            var streetName =
                (address.StreetName ?? "").Trim();

            var streetSuffix =
                (address.StreetSuffix ?? "").Trim();

            var combined =
                string.Join(
                    " ",
                    new[]
                    {
                        streetName,
                        streetSuffix
                    }
                    .Where(
                        value =>
                            !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(combined)
                ? null
                : combined;
        }

        private static DateTimeOffset ConvertCacheTimeToUtc(DateTime cachedAt)
        {
            var utcValue =
                cachedAt.Kind switch
                {
                    DateTimeKind.Utc =>
                        cachedAt,

                    DateTimeKind.Local =>
                        cachedAt.ToUniversalTime(),

                    _ =>
                        DateTime.SpecifyKind(
                            cachedAt,
                            DateTimeKind.Utc)
                };

            return new DateTimeOffset(utcValue);
        }

        private static string NormalizeSiteId(string? siteId)
        {
            return (siteId ?? "")
                .Trim()
                .ToUpperInvariant();
        }

        private static IEnumerable<string>GetLookupSiteIdCandidates(string siteId)
        {
            yield return siteId;

            /*
             * Preserve the same RX/RE compatibility behavior
             * used by the live Parent DB lookup.
             */
            if (siteId.StartsWith(
                    "RX",
                    StringComparison.OrdinalIgnoreCase) &&
                siteId.Length > 2)
            {
                yield return "RE" + siteId[2..];
            }
            else if (siteId.StartsWith(
                         "RE",
                         StringComparison.OrdinalIgnoreCase) &&
                     siteId.Length > 2)
            {
                yield return "RX" + siteId[2..];
            }
        }
    }
}