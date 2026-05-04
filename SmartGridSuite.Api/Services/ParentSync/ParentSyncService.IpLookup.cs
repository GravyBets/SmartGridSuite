using Microsoft.Data.SqlClient;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<AssociatedSiteByIpLookupDto> FindAssociatedSiteByIpAsync(
            string ip,
            CancellationToken cancellationToken = default)
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
            SELECT TOP (1)
                SiteId,
                MatchSource,
                MatchField,
                IpAddress,
                (SELECT COUNT(*) FROM DistinctSiteMatches) AS MatchCount
            FROM DistinctSiteMatches
            ORDER BY MatchPriority, SiteId;
            """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Ip", SqlDbType.NVarChar, 50) { Value = ip });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return empty;

            return new AssociatedSiteByIpLookupDto
            {
                IpAddress = GetString(reader, "IpAddress") ?? ip,
                Found = true,
                SiteId = GetString(reader, "SiteId") ?? "",
                MatchSource = GetString(reader, "MatchSource") ?? "",
                MatchField = GetString(reader, "MatchField") ?? "",
                MatchCount = GetNullableInt32(reader, "MatchCount") ?? 1
            };
        }
    }
}