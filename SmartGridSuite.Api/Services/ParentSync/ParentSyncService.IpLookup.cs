using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<AssociatedSiteByIpLookupDto> FindAssociatedSiteByIpAsync(
            string ip, CancellationToken cancellationToken = default)
        {
            ip = (ip ?? string.Empty).Trim();

            var empty = new AssociatedSiteByIpLookupDto
            {
                IpAddress = ip,
                Found = false
            };

            if (string.IsNullOrWhiteSpace(ip))
                return empty;

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
            WITH RawMatches AS
            (
                -- AMS / MR
                SELECT
                    a.SiteId,
                    'AMS/MR' AS MatchSource,
                    'RadioIP' AS MatchField,
                    a.RadioIP AS IpAddress,
                    10 AS MatchPriority
                FROM [sgc_comm].[AMS] a
                WHERE LTRIM(RTRIM(ISNULL(a.RadioIP, ''))) = @Ip

                UNION ALL

                SELECT
                    a.SiteId,
                    'AMS/MR' AS MatchSource,
                    'EthernetIP' AS MatchField,
                    a.EthernetIP AS IpAddress,
                    20 AS MatchPriority
                FROM [sgc_comm].[AMS] a
                WHERE LTRIM(RTRIM(ISNULL(a.EthernetIP, ''))) = @Ip

                UNION ALL

                SELECT
                    l.SiteId,
                    'LTE' AS MatchSource,
                    'IP1' AS MatchField,
                    l.IP1 AS IpAddress,
                    30 AS MatchPriority
                FROM [sgc_equip].[LTE] l
                WHERE LTRIM(RTRIM(ISNULL(l.IP1, ''))) = @Ip

                -- IGSD
                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'RadioIP' AS MatchField,
                    i.RadioIP AS IpAddress,
                    40 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'PriProtLanDigi' AS MatchField,
                    i.PriProtLanDigi AS IpAddress,
                    41 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.PriProtLanDigi, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'PriWanOut' AS MatchField,
                    i.PriWanOut AS IpAddress,
                    42 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.PriWanOut, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'PriDigiWanOut' AS MatchField,
                    i.PriDigiWanOut AS IpAddress,
                    43 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.PriDigiWanOut, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'PriProtLanSubN' AS MatchField,
                    i.PriProtLanSubN AS IpAddress,
                    44 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.PriProtLanSubN, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'PriProtLanRtu' AS MatchField,
                    i.PriProtLanRtu AS IpAddress,
                    45 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.PriProtLanRtu, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'SecDigiWanOut' AS MatchField,
                    i.SecDigiWanOut AS IpAddress,
                    46 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.SecDigiWanOut, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'SecProtLanDigi' AS MatchField,
                    i.SecProtLanDigi AS IpAddress,
                    47 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.SecProtLanDigi, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'SecProtLanSubN' AS MatchField,
                    i.SecProtLanSubN AS IpAddress,
                    48 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.SecProtLanSubN, ''))) = @Ip

                UNION ALL

                SELECT
                    i.SiteId,
                    'IGSD' AS MatchSource,
                    'SecProtLanRtu' AS MatchField,
                    i.SecProtLanRtu AS IpAddress,
                    49 AS MatchPriority
                FROM [sgc_comm].[IGSD] i
                WHERE LTRIM(RTRIM(ISNULL(i.SecProtLanRtu, ''))) = @Ip

                -- DACs
                UNION ALL

                SELECT
                    r.SiteId,
                    'DACS' AS MatchSource,
                    'RadioIP' AS MatchField,
                    r.RadioIP AS IpAddress,
                    60 AS MatchPriority
                FROM [sgc_equip].[Radio700] r
                WHERE LTRIM(RTRIM(ISNULL(r.RadioIP, ''))) = @Ip

                UNION ALL

                SELECT
                    r.SiteId,
                    'DACS' AS MatchSource,
                    'RtuWanVLAN' AS MatchField,
                    r.RtuWanVLAN AS IpAddress,
                    61 AS MatchPriority
                FROM [sgc_equip].[Radio700] r
                WHERE LTRIM(RTRIM(ISNULL(r.RtuWanVLAN, ''))) = @Ip

                UNION ALL

                SELECT
                    r.SiteId,
                    'DACS' AS MatchSource,
                    'RtuWanVLANGateway' AS MatchField,
                    r.RtuWanVLANGateway AS IpAddress,
                    62 AS MatchPriority
                FROM [sgc_equip].[Radio700] r
                WHERE LTRIM(RTRIM(ISNULL(r.RtuWanVLANGateway, ''))) = @Ip
            ),
            CleanMatches AS
            (
                SELECT
                    SiteId,
                    MatchSource,
                    MatchField,
                    IpAddress,
                    MatchPriority,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY SiteId
                        ORDER BY MatchPriority
                    ) AS SiteRank
                FROM RawMatches
                WHERE SiteId IS NOT NULL
                  AND LTRIM(RTRIM(ISNULL(SiteId, ''))) <> ''
                  AND IpAddress IS NOT NULL
                  AND LTRIM(RTRIM(ISNULL(IpAddress, ''))) <> ''
                  AND UPPER(LTRIM(RTRIM(ISNULL(IpAddress, '')))) <> 'NA'
            ),
            DistinctSiteMatches AS
            (
                SELECT
                    SiteId,
                    MatchSource,
                    MatchField,
                    IpAddress,
                    MatchPriority
                FROM CleanMatches
                WHERE SiteRank = 1
            )
            SELECT
                SiteId,
                MatchSource,
                MatchField,
                IpAddress
            FROM DistinctSiteMatches
            ORDER BY MatchPriority, SiteId;
            """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Ip", SqlDbType.NVarChar, 50) { Value = ip });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var matches =
                new List<AssociatedSiteByIpMatchDto>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId =
                    GetString(reader, "SiteId") ?? "";

                var matchSource =
                    GetString(reader, "MatchSource") ?? "";

                var matchField =
                    GetString(reader, "MatchField") ?? "";

                if (string.IsNullOrWhiteSpace(siteId))
                    continue;

                matches.Add(
                    new AssociatedSiteByIpMatchDto
                    {
                        SiteId = siteId,
                        DashboardKind =
                            GetDashboardKindForIpMatchSource(
                                matchSource),
                        MatchSource = matchSource,
                        MatchField = matchField
                    });
            }

            return BuildAssociatedSiteByIpLookupResult(
                ip,
                matches,
                isCached: false,
                warning: "");
        }

        public async Task<AssociatedSiteByIpLookupDto> FindAssociatedSiteByIpFromCacheAsync(
        string ip,
        CancellationToken cancellationToken = default)
        {
            ip = (ip ?? string.Empty).Trim();

            const string warning =
                "The parent database lookup was unavailable. " +
                "Results are from cached SmartGridSuite site data.";

            if (string.IsNullOrWhiteSpace(ip))
            {
                return new AssociatedSiteByIpLookupDto
                {
                    IpAddress = ip,
                    Found = false,
                    IsCached = true,
                    Warning = warning
                };
            }

            var candidates =
                new List<(
                    AssociatedSiteByIpMatchDto Match,
                    int Priority)>();

            var amsRows = await _appDb.CacheSiteAms
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    (x.PrimaryCommsIp == ip ||
                     x.SecondaryLanIp == ip))
                .ToListAsync(cancellationToken);

            foreach (var row in amsRows)
            {
                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryCommsIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.AmsMr,
                    "AMS/MR",
                    "RadioIP",
                    10);

                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryLanIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.AmsMr,
                    "AMS/MR",
                    "EthernetIP",
                    11);
            }

            var lteRows = await _appDb.CacheSiteLte
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    (x.SecondaryWanIp == ip ||
                     x.SecondaryWanIp2 == ip))
                .ToListAsync(cancellationToken);

            foreach (var row in lteRows)
            {
                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryWanIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.AmsMr,
                    "LTE",
                    "IP1",
                    20);

                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryWanIp2,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.AmsMr,
                    "LTE",
                    "IP2",
                    21);
            }

            var igsdRows = await _appDb.CacheSiteIgsd
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    (x.PrimaryCommsIp == ip ||
                     x.PrimaryLanIp == ip ||
                     x.PrimaryWanIp == ip ||
                     x.PrimaryTunnelIp == ip ||
                     x.PrimaryRtuIp == ip ||
                     x.SecondaryWanIp == ip ||
                     x.SecondaryLanIp == ip ||
                     x.SecondaryTunnelIp == ip ||
                     x.SecondaryRtuIp == ip))
                .ToListAsync(cancellationToken);

            foreach (var row in igsdRows)
            {
                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryCommsIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "RadioIP",
                    30);

                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryLanIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "PriProtLanDigi",
                    31);

                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryWanIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "PriWanOut",
                    32);

                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryTunnelIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "PriProtLanSubN",
                    33);

                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryRtuIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "PriProtLanRtu",
                    34);

                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryWanIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "SecDigiWanOut",
                    35);

                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryLanIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "SecProtLanDigi",
                    36);

                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryTunnelIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "SecProtLanSubN",
                    37);

                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryRtuIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Igsd,
                    "IGSD",
                    "SecProtLanRtu",
                    38);
            }

            var dacsRows = await _appDb.CacheSiteDacs
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    (x.PrimaryCommsIp == ip ||
                     x.TunnelIp == ip ||
                     x.RtuIp == ip))
                .ToListAsync(cancellationToken);

            foreach (var row in dacsRows)
            {
                AddAssociatedIpCandidate(
                    candidates,
                    row.PrimaryCommsIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Dacs,
                    "DACS",
                    "RadioIP",
                    60);

                AddAssociatedIpCandidate(
                    candidates,
                    row.TunnelIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Dacs,
                    "DACS",
                    "RtuWanVLAN",
                    61);

                AddAssociatedIpCandidate(
                    candidates,
                    row.RtuIp,
                    ip,
                    row.SiteId,
                    SiteDashboardKinds.Dacs,
                    "DACS",
                    "RtuWanVLANGateway",
                    62);
            }

            var matches = candidates
                .GroupBy(
                    x => x.Match.SiteId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderBy(x => x.Priority)
                        .First())
                .OrderBy(x => x.Priority)
                .ThenBy(
                    x => x.Match.SiteId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Match)
                .ToList();

            return BuildAssociatedSiteByIpLookupResult(
                ip,
                matches,
                isCached: true,
                warning);
        }

        public async Task<AssociatedSiteByIpLookupDto>FindAssociatedSiteBySerialAsync(
            string serialNumber,
            CancellationToken cancellationToken = default)
        {
            serialNumber =
                (serialNumber ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return new AssociatedSiteByIpLookupDto
                {
                    IpAddress = serialNumber,
                    Found = false
                };
            }

            await using var conn =
                new SqlConnection(_connectionString);

            await conn.OpenAsync(cancellationToken);

            const string sql = """
        WITH RawMatches AS
        (
            -- PMR serial number attached to an AMS / MR site.
            SELECT
                a.SiteId,
                'AMS/MR' AS MatchSource,
                'PMR SN' AS MatchField,
                10 AS MatchPriority
            FROM [sgc_comm].[AMS] a
            WHERE LTRIM(RTRIM(
                CONVERT(nvarchar(150), a.iTron_CR_Num))) = @SerialNumber

            UNION ALL

            -- RX serial number is stored as RE.MeterNumber.
            SELECT
                r.SiteId,
                'RX' AS MatchSource,
                'RX SN' AS MatchField,
                20 AS MatchPriority
            FROM [sgc_comm].[RE] r
            WHERE LTRIM(RTRIM(
                CONVERT(nvarchar(150), r.MeterNumber))) = @SerialNumber
        ),
        CleanMatches AS
        (
            SELECT
                SiteId,
                MatchSource,
                MatchField,
                MatchPriority,
                ROW_NUMBER() OVER
                (
                    PARTITION BY SiteId
                    ORDER BY MatchPriority
                ) AS SiteRank
            FROM RawMatches
            WHERE SiteId IS NOT NULL
              AND LTRIM(RTRIM(
                    ISNULL(SiteId, ''))) <> ''
        )
        SELECT
            SiteId,
            MatchSource,
            MatchField
        FROM CleanMatches
        WHERE SiteRank = 1
        ORDER BY MatchPriority, SiteId;
        """;

            await using var cmd =
                new SqlCommand(sql, conn);

            cmd.Parameters.Add(
                new SqlParameter(
                    "@SerialNumber",
                    SqlDbType.NVarChar,
                    150)
                {
                    Value = serialNumber
                });

            await using var reader =
                await cmd.ExecuteReaderAsync(
                    cancellationToken);

            var matches =
                new List<AssociatedSiteByIpMatchDto>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId =
                    GetString(reader, "SiteId") ?? "";

                var matchSource =
                    GetString(reader, "MatchSource") ?? "";

                var matchField =
                    GetString(reader, "MatchField") ?? "";

                if (string.IsNullOrWhiteSpace(siteId))
                    continue;

                matches.Add(
                    new AssociatedSiteByIpMatchDto
                    {
                        SiteId = siteId,

                        DashboardKind =
                            string.Equals(
                                matchSource,
                                "AMS/MR",
                                StringComparison.OrdinalIgnoreCase)
                                ? SiteDashboardKinds.AmsMr
                                : "RX",

                        MatchSource = matchSource,
                        MatchField = matchField
                    });
            }

            // IpAddress remains populated for backward compatibility with
            // the existing lookup response contract.
            return BuildAssociatedSiteByIpLookupResult(
                serialNumber,
                matches,
                isCached: false,
                warning: "");
        }

        public async Task<AssociatedSiteByIpLookupDto>FindAssociatedSiteBySerialFromCacheAsync(
                string serialNumber,
                CancellationToken cancellationToken = default)
        {
            serialNumber =
                (serialNumber ?? string.Empty).Trim();

            const string warning =
                "The parent database lookup was unavailable. " +
                "Results are from cached SmartGridSuite site data.";

            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return new AssociatedSiteByIpLookupDto
                {
                    IpAddress = serialNumber,
                    Found = false,
                    IsCached = true,
                    Warning = warning
                };
            }

            var candidates =
                new List<(
                    AssociatedSiteByIpMatchDto Match,
                    int Priority)>();

            var pmrRows =
                await _appDb.CacheSitePmr
                    .AsNoTracking()
                    .Where(x =>
                        x.IsActive &&
                        x.SecondaryCommsIdentifier ==
                            serialNumber)
                    .ToListAsync(cancellationToken);

            foreach (var row in pmrRows)
            {
                AddAssociatedIpCandidate(
                    candidates,
                    row.SecondaryCommsIdentifier,
                    serialNumber,
                    row.SiteId,
                    SiteDashboardKinds.AmsMr,
                    "AMS/MR",
                    "PMR SN",
                    10);
            }

            var rxRows =
                await _appDb.CacheSiteRx
                    .AsNoTracking()
                    .Where(x =>
                        x.IsActive &&
                        x.MeterNumber == serialNumber)
                    .ToListAsync(cancellationToken);

            foreach (var row in rxRows)
            {
                AddAssociatedIpCandidate(
                    candidates,
                    row.MeterNumber,
                    serialNumber,
                    row.SiteId,
                    "RX",
                    "RX",
                    "RX SN",
                    20);
            }

            var matches =
                candidates
                    .GroupBy(
                        x => x.Match.SiteId,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                        group
                            .OrderBy(x => x.Priority)
                            .First())
                    .OrderBy(x => x.Priority)
                    .ThenBy(
                        x => x.Match.SiteId,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Match)
                    .ToList();

            return BuildAssociatedSiteByIpLookupResult(
                serialNumber,
                matches,
                isCached: true,
                warning);
        }

        private static void AddAssociatedIpCandidate(ICollection<(AssociatedSiteByIpMatchDto Match, int Priority)> 
            candidates,
            string? cachedIp,
            string requestedIp,
            string? siteId,
            string dashboardKind,
            string matchSource,
            string matchField,
            int priority)
        {
            var cleanCachedIp =
                (cachedIp ?? string.Empty).Trim();

            var cleanSiteId =
                (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanSiteId))
                return;

            if (!string.Equals(
                    cleanCachedIp,
                    requestedIp,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            candidates.Add(
                (
                    new AssociatedSiteByIpMatchDto
                    {
                        SiteId = cleanSiteId,
                        DashboardKind = dashboardKind,
                        MatchSource = matchSource,
                        MatchField = matchField
                    },
                    priority
                ));
        }

        private static AssociatedSiteByIpLookupDto BuildAssociatedSiteByIpLookupResult(
            string ip,
            IEnumerable<AssociatedSiteByIpMatchDto> matches,
            bool isCached,
            string warning)
        {
            var orderedMatches = matches
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.SiteId))
                .GroupBy(
                    x => x.SiteId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (orderedMatches.Count == 0)
            {
                return new AssociatedSiteByIpLookupDto
                {
                    IpAddress = ip,
                    Found = false,
                    IsCached = isCached,
                    Warning = warning,
                    MatchCount = 0,
                    Matches = new List<AssociatedSiteByIpMatchDto>()
                };
            }

            var first = orderedMatches[0];

            return new AssociatedSiteByIpLookupDto
            {
                IpAddress = ip,
                Found = true,
                IsCached = isCached,
                Warning = warning,

                SiteId = first.SiteId,
                MatchSource = first.MatchSource,
                MatchField = first.MatchField,
                MatchCount = orderedMatches.Count,

                Matches = orderedMatches
            };
        }

        private static string GetDashboardKindForIpMatchSource(
            string? matchSource)
        {
            var source =
                (matchSource ?? string.Empty).Trim();

            if (source.Equals(
                    "AMS/MR",
                    StringComparison.OrdinalIgnoreCase) ||
                source.Equals(
                    "LTE",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.AmsMr;
            }

            if (source.Equals(
                    "IGSD",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.Igsd;
            }

            if (source.Equals(
                    "DACS",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SiteDashboardKinds.Dacs;
            }

            return string.Empty;
        }
    }
}