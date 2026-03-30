using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using System.Data.Common;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        private readonly string _connectionString;
        private readonly SmartGridDbContext _appDb;

        public ParentSyncService(
            IOptions<ParentDatabaseOptions> options,
            SmartGridDbContext appDb)
        {
            _connectionString = options.Value.ConnectionString;
            _appDb = appDb;
        }

        public async Task<int> GetSiteCountAsync(CancellationToken cancellationToken = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            const string sql = "SELECT COUNT(*) FROM [sgc_main].[Site];";

            await using var cmd = new SqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            return result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        //To see which model to run (AMS, DACs, IG...)
        public async Task<SiteDashboardRouteInfo?> GetSiteDashboardRouteInfoAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
                SELECT
                    s.SiteId,
                    s.SiteType_id AS SiteTypeId,
                    dt.CodeValue  AS SiteType,
                    s.Config_id   AS ConfigId,
                    cfg.Config    AS SiteConfigName,
                    cfg.Comm1Type AS PrimaryCommType,
                    cfg.Comm2Type AS SecondaryCommType,
                    cfg.Descr     AS SiteConfigDescription
                FROM [sgc_main].[Site] s
                LEFT JOIN [sgc_main].[Decode] dt
                    ON s.SiteType_id = dt.CodeId
                   AND dt.CodeType = 'SiteType'
                LEFT JOIN [sgc_main].[Config] cfg
                    ON s.Config_id = cfg.id
                WHERE s.SiteId = @SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@SiteId", SqlDbType.NVarChar, 50) { Value = siteId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new SiteDashboardRouteInfo
            {
                SiteId = GetString(reader, "SiteId") ?? "",
                SiteTypeId = GetNullableInt32(reader, "SiteTypeId"),
                SiteType = GetString(reader, "SiteType"),
                ConfigId = GetNullableInt32(reader, "ConfigId"),
                SiteConfigName = GetString(reader, "SiteConfigName"),
                PrimaryCommType = GetString(reader, "PrimaryCommType"),
                SecondaryCommType = GetString(reader, "SecondaryCommType"),
                SiteConfigDescription = GetString(reader, "SiteConfigDescription")
            };
        }

        public async Task<object?> GetSiteDashboardAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return null;
            }

            var route = await GetSiteDashboardRouteInfoAsync(siteId, cancellationToken);

            if (route is null)
            {
                return null;
            }

            var dashboardKind = ResolveDashboardKind(route);

            return dashboardKind switch
            {
                "ams-mr" => await GetAmsMrSiteAsync(siteId, cancellationToken),
                "dacs" => await GetDacsSiteAsync(siteId, cancellationToken),
                "igsd" => await GetIgsdSiteAsync(siteId, cancellationToken),
                _ => null
            };
        }

        //AMS Sites
        public async Task<AmsMrSiteDashboardRow?> GetAmsMrSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
                SELECT
                    a.SiteId,
                    d.CodeValue    AS SiteStatus,
                    dt.CodeValue   AS SiteType,
                    cfg.Config     AS SiteConfigName,
                    cfg.Comm1Type  AS PrimaryCommType,
                    cfg.Comm2Type  AS SecondaryCommType,
                    cfg.Descr      AS SiteConfigDescription,
                    a.RadioSN      AS PrimaryCommsIdentifier,
                    a.RadioIP      AS PrimaryCommsIp,
                    a.EthernetIP   AS SecondaryLanIp,
                    l.IP1          AS SecondaryWanIp,
                    a.iTron_CR_Num AS SecondaryCommsIdentifier,
                    p.UserName     AS SecondaryCommsUsername,
                    p.wifiSSID     AS SecondaryCommsSsid,
                    p.CAMPassword  AS SecondaryCommsPassword,
                    p.ATTSlot1     AS SecondarySimNumber,

                    ant.SN         AS AntennaSerialNumber,
                    enc.SN         AS EnclosureSerialNumber,
                    enc.Model      AS EnclosureModel,
                    tn.TopName     AS TopName,
                    tn.Descr       AS TopDescription,
                    ts.Sector      AS TopSector,

                    addr.StreetNo,
                    addr.StreetName,
                    addr.City,
                    addr.County,
                    addr.StateCode,
                    addr.ZipCode,

                    gps.Latitude,
                    gps.Longitude

                FROM [sgc_comm].[AMS] a
                LEFT JOIN [sgc_equip].[PMR] p
                    ON a.iTron_CR_Num = p.SN
                LEFT JOIN [sgc_equip].[LTE] l
                    ON a.SiteId = l.SiteId
                LEFT JOIN [sgc_equip].[Antenna] ant
                    ON a.SiteId = ant.SiteId
                LEFT JOIN [sgc_equip].[Enclosure] enc
                    ON a.SiteId = enc.SiteId
                LEFT JOIN [sgc_main].[Site] s
                    ON a.SiteId = s.SiteId
                LEFT JOIN [sgc_main].[Decode] d
                    ON s.SiteStatus_id = d.CodeId
                   AND d.CodeType = 'SiteStatus'
                LEFT JOIN [sgc_main].[Decode] dt
                    ON s.SiteType_id = dt.CodeId
                   AND dt.CodeType = 'SiteType'
                LEFT JOIN [sgc_main].[Config] cfg
                    ON s.Config_id = cfg.id
                LEFT JOIN [sgc_tnp].[TopSite] ts
                    ON s.TowerAp_id = ts.id
                LEFT JOIN [sgc_tnp].[TopName] tn
                    ON ts.TopName_id = tn.id
                LEFT JOIN [sgc_main].[Address] addr
                    ON a.SiteId = addr.SiteId
                LEFT JOIN [sgc_main].[GPS] gps
                    ON a.SiteId = gps.SiteId
                WHERE a.SiteId = @SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@SiteId", SqlDbType.NVarChar, 50) { Value = siteId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var row = new AmsMrSiteDashboardRow
            {
                SiteId = GetString(reader, "SiteId") ?? "",
                SiteStatus = GetString(reader, "SiteStatus"),                
                SiteType = GetString(reader, "SiteType"),
                SiteConfigName = GetString(reader, "SiteConfigName"),
                PrimaryCommType = GetString(reader, "PrimaryCommType"),
                SecondaryCommType = GetString(reader, "SecondaryCommType"),
                SiteConfigDescription = GetString(reader, "SiteConfigDescription"),

                PrimaryCommsIdentifier = GetString(reader, "PrimaryCommsIdentifier"),
                PrimaryCommsIp = GetString(reader, "PrimaryCommsIp"),

                SecondaryLanIp = GetString(reader, "SecondaryLanIp"),
                SecondaryWanIp = GetString(reader, "SecondaryWanIp"),

                SecondaryCommsIdentifier = GetString(reader, "SecondaryCommsIdentifier"),
                SecondaryCommsUsername = GetString(reader, "SecondaryCommsUsername"),
                SecondaryCommsSsid = GetString(reader, "SecondaryCommsSsid"),
                SecondaryCommsPassword = GetString(reader, "SecondaryCommsPassword"),
                SecondarySimNumber = GetString(reader, "SecondarySimNumber"),

                AntennaSerialNumber = GetString(reader, "AntennaSerialNumber"),
                EnclosureSerialNumber = GetString(reader, "EnclosureSerialNumber"),
                EnclosureModel = GetString(reader, "EnclosureModel"),

                TopName = GetString(reader, "TopName"),
                TopDescription = GetString(reader, "TopDescription"),
                TopSector = GetString(reader, "TopSector"),

                StreetNo = GetString(reader, "StreetNo"),
                StreetName = GetString(reader, "StreetName"),
                City = GetString(reader, "City"),
                County = GetString(reader, "County"),
                StateCode = GetString(reader, "StateCode"),
                ZipCode = GetString(reader, "ZipCode"),

                Latitude = GetDecimal(reader, "Latitude"),
                Longitude = GetDecimal(reader, "Longitude")
            };

            var historyLookup = await GetRecentSiteHistoryLookupAsync(
                new[] { row.SiteId },
                cancellationToken);

            if (historyLookup.TryGetValue(row.SiteId, out var history))
            {
                row.History.AddRange(history);
            }

            return row;
        }

        
        private async Task<Dictionary<string, List<SiteHistoryPreviewRow>>> GetRecentSiteHistoryLookupAsync(
                IEnumerable<string> siteIds, CancellationToken cancellationToken = default)
        {
                    var distinctSiteIds = siteIds
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var lookup = new Dictionary<string, List<SiteHistoryPreviewRow>>(StringComparer.OrdinalIgnoreCase);

                    if (distinctSiteIds.Count == 0)
                    {
                        return lookup;
                    }

                    var conn = _appDb.Database.GetDbConnection();
                    var shouldClose = conn.State != ConnectionState.Open;

                    if (shouldClose)
                    {
                        await conn.OpenAsync(cancellationToken);
                    }

                    try
                    {
                        await using var cmd = conn.CreateCommand();

                        var parameterNames = new List<string>();

                        for (var i = 0; i < distinctSiteIds.Count; i++)
                        {
                            var parameter = cmd.CreateParameter();
                            parameter.ParameterName = $"@p{i}";
                            parameter.Value = distinctSiteIds[i];
                            cmd.Parameters.Add(parameter);
                            parameterNames.Add(parameter.ParameterName);
                        }

                        cmd.CommandText = $"""
                            SELECT
                                history_id,
                                site_id,
                                visit_date,
                                primary_tech,
                                secondary_tech,
                                narrative
                            FROM site_history
                            WHERE site_id IN ({string.Join(", ", parameterNames)})
                            ORDER BY site_id, visit_date DESC, history_id DESC;
                            """;

                        var allRows = new List<SiteHistoryPreviewRow>();

                        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                        while (await reader.ReadAsync(cancellationToken))
                        {
                            allRows.Add(new SiteHistoryPreviewRow
                            {
                                HistoryId = GetInt64(reader, "history_id"),
                                SiteId = GetDbString(reader, "site_id") ?? "",
                                VisitDate = GetNullableDateTime(reader, "visit_date"),
                                PrimaryTech = GetDbString(reader, "primary_tech"),
                                SecondaryTech = GetDbString(reader, "secondary_tech"),
                                Narrative = GetDbString(reader, "narrative")
                            });
                        }

                        foreach (var group in allRows.GroupBy(x => x.SiteId, StringComparer.OrdinalIgnoreCase))
                        {
                            lookup[group.Key] = group.ToList();
                        }

                        return lookup;
                    }
                    finally
                    {
                        if (shouldClose)
                        {
                            await conn.CloseAsync();
                        }
                    }
                }

        //DACs Sites
        public async Task<DacsSiteDashboardRow?> GetDacsSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
                SELECT
                    r.SiteId,
                    d.CodeValue         AS SiteStatus,
                    dt.CodeValue        AS SiteType,
                    cfg.Config          AS SiteConfigName,
                    cfg.Comm1Type       AS PrimaryCommType,
                    cfg.Comm2Type       AS SecondaryCommType,
                    cfg.Descr           AS SiteConfigDescription,
                    r.RadioIP           AS PrimaryCommsIp,
                    r.RtuWanVLAN        AS TunnelIp,
                    r.RtuWanVLANGateway AS RtuIp,

                    tn.TopName          AS TopName,
                    tn.Descr            AS TopDescription,
                    ts.Sector           AS TopSector,

                    addr.StreetNo,
                    addr.StreetName,
                    addr.City,
                    addr.County,
                    addr.StateCode,
                    addr.ZipCode,

                    gps.Latitude,
                    gps.Longitude


                FROM [sgc_equip].[Radio700] r
                LEFT JOIN [sgc_main].[Site] s
                    ON r.SiteId = s.SiteId
                LEFT JOIN [sgc_main].[Decode] d
                    ON s.SiteStatus_id = d.CodeId
                   AND d.CodeType = 'SiteStatus'
                LEFT JOIN [sgc_main].[Decode] dt
                    ON s.SiteType_id = dt.CodeId
                   AND dt.CodeType = 'SiteType'
                LEFT JOIN [sgc_main].[Config] cfg
                    ON s.Config_id = cfg.id
                LEFT JOIN [sgc_tnp].[TopSite] ts
                    ON s.TowerAp_id = ts.id
                LEFT JOIN [sgc_tnp].[TopName] tn
                    ON ts.TopName_id = tn.id
                LEFT JOIN [sgc_main].[Address] addr
                    ON r.SiteId = addr.SiteId
                LEFT JOIN [sgc_main].[GPS] gps
                    ON r.SiteId = gps.SiteId
                WHERE r.SiteId = @SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@SiteId", SqlDbType.NVarChar, 50) { Value = siteId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var row = new DacsSiteDashboardRow
            {
                SiteId = GetString(reader, "SiteId") ?? "",
                SiteStatus = GetString(reader, "SiteStatus"),
                SiteType = GetString(reader, "SiteType"),
                SiteConfigName = GetString(reader, "SiteConfigName"),
                PrimaryCommType = GetString(reader, "PrimaryCommType"),
                SecondaryCommType = GetString(reader, "SecondaryCommType"),
                SiteConfigDescription = GetString(reader, "SiteConfigDescription"),

                PrimaryCommsIp = GetString(reader, "PrimaryCommsIp"),
                TunnelIp = GetString(reader, "TunnelIp"),
                RtuIp = GetString(reader, "RtuIp"),

                TopName = GetString(reader, "TopName"),
                TopDescription = GetString(reader, "TopDescription"),
                TopSector = GetString(reader, "TopSector"),

                StreetNo = GetString(reader, "StreetNo"),
                StreetName = GetString(reader, "StreetName"),
                City = GetString(reader, "City"),
                County = GetString(reader, "County"),
                StateCode = GetString(reader, "StateCode"),
                ZipCode = GetString(reader, "ZipCode"),

                Latitude = GetDecimal(reader, "Latitude"),
                Longitude = GetDecimal(reader, "Longitude")
            };

            var historyLookup = await GetRecentSiteHistoryLookupAsync(
                new[] { row.SiteId },
                cancellationToken);

            if (historyLookup.TryGetValue(row.SiteId, out var history))
            {
                row.History.AddRange(history);
            }

            return row;
        }

        //IGSD Sites
        public async Task<IgsdSiteDashboardRow?> GetIgsdSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
                SELECT
                    i.SiteId,
                    d.CodeValue        AS SiteStatus,
                    dt.CodeValue       AS SiteType,
                    cfg.Config         AS SiteConfigName,
                    cfg.Comm1Type      AS PrimaryCommType,
                    cfg.Comm2Type      AS SecondaryCommType,
                    cfg.Descr          AS SiteConfigDescription,

                    CASE
                        WHEN UPPER(LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN NULL
                        ELSE i.RadioSN
                    END                AS PrimaryCommsIdentifier,

                    CASE
                        WHEN UPPER(LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN NULL
                        ELSE i.RadioIP
                    END                AS PrimaryCommsIp,

                    i.PriProtLanDigi   AS PrimaryLanIp,

                    CASE
                        WHEN UPPER(LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN i.PriDigiWanOut
                        ELSE i.PriWanOut
                    END                AS PrimaryWanIp,

                    CASE
                        WHEN UPPER(LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN i.PriProtLanSubN
                        ELSE i.PriDigiWanOut
                    END                AS PrimaryTunnelIp,

                    i.PriProtLanRtu    AS PrimaryRtuIp,

                    l.SN               AS SecondaryCommsIdentifier,
                    i.SecDigiWanOut    AS SecondaryWanIp,
                    i.SecProtLanDigi   AS SecondaryLanIp,
                    i.SecProtLanSubN   AS SecondaryTunnelIp,
                    i.SecProtLanRtu    AS SecondaryRtuIp,

                    ant.SN             AS AntennaSerialNumber,
                    enc.SN             AS EnclosureSerialNumber,
                    enc.Model          AS EnclosureModel,
                    i.Cyberlock        AS CyberlockSerialNumber,
                    sec.PSK            AS TunnelPsk,

                    tn.TopName         AS TopName,
                    tn.Descr           AS TopDescription,
                    ts.Sector          AS TopSector,

                    addr.StreetNo,
                    addr.StreetName,
                    addr.City,
                    addr.County,
                    addr.StateCode,
                    addr.ZipCode,

                    gps.Latitude,
                    gps.Longitude


                FROM [sgc_comm].[IGSD] i
                LEFT JOIN [sgc_comm].[IGSD_Secure] sec
                    ON i.SiteId = sec.SiteId
                LEFT JOIN [sgc_equip].[LTE] l
                    ON i.SiteId = l.SiteId
                LEFT JOIN [sgc_equip].[Antenna] ant
                    ON i.SiteId = ant.SiteId
                LEFT JOIN [sgc_equip].[Enclosure] enc
                    ON i.SiteId = enc.SiteId
                LEFT JOIN [sgc_main].[Site] s
                    ON i.SiteId = s.SiteId
                LEFT JOIN [sgc_main].[Decode] d
                    ON s.SiteStatus_id = d.CodeId
                   AND d.CodeType = 'SiteStatus'
                LEFT JOIN [sgc_main].[Decode] dt
                    ON s.SiteType_id = dt.CodeId
                   AND dt.CodeType = 'SiteType'
                LEFT JOIN [sgc_main].[Config] cfg
                    ON s.Config_id = cfg.id
                LEFT JOIN [sgc_tnp].[TopSite] ts
                    ON s.TowerAp_id = ts.id
                LEFT JOIN [sgc_tnp].[TopName] tn
                    ON ts.TopName_id = tn.id
                LEFT JOIN [sgc_main].[Address] addr
                    ON i.SiteId = addr.SiteId
                LEFT JOIN [sgc_main].[GPS] gps
                    ON i.SiteId = gps.SiteId
                WHERE i.SiteId = @SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@SiteId", SqlDbType.NVarChar, 50) { Value = siteId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var row = new IgsdSiteDashboardRow
            {
                SiteId = GetString(reader, "SiteId") ?? "",

                SiteStatus = GetString(reader, "SiteStatus"),
                SiteType = GetString(reader, "SiteType"),
                SiteConfigName = GetString(reader, "SiteConfigName"),
                PrimaryCommType = GetString(reader, "PrimaryCommType"),
                SecondaryCommType = GetString(reader, "SecondaryCommType"),
                SiteConfigDescription = GetString(reader, "SiteConfigDescription"),

                PrimaryCommsIdentifier = GetString(reader, "PrimaryCommsIdentifier"),
                PrimaryCommsIp = GetString(reader, "PrimaryCommsIp"),
                PrimaryLanIp = GetString(reader, "PrimaryLanIp"),
                PrimaryWanIp = GetString(reader, "PrimaryWanIp"),
                PrimaryTunnelIp = GetString(reader, "PrimaryTunnelIp"),
                PrimaryRtuIp = GetString(reader, "PrimaryRtuIp"),

                SecondaryCommsIdentifier = GetString(reader, "SecondaryCommsIdentifier"),
                SecondaryWanIp = GetString(reader, "SecondaryWanIp"),
                SecondaryLanIp = GetString(reader, "SecondaryLanIp"),
                SecondaryTunnelIp = GetString(reader, "SecondaryTunnelIp"),
                SecondaryRtuIp = GetString(reader, "SecondaryRtuIp"),

                AntennaSerialNumber = GetString(reader, "AntennaSerialNumber"),
                EnclosureSerialNumber = GetString(reader, "EnclosureSerialNumber"),
                EnclosureModel = GetString(reader, "EnclosureModel"),
                CyberlockSerialNumber = GetString(reader, "CyberlockSerialNumber"),
                TunnelPsk = GetString(reader, "TunnelPsk"),

                TopName = GetString(reader, "TopName"),
                TopDescription = GetString(reader, "TopDescription"),
                TopSector = GetString(reader, "TopSector"),

                StreetNo = GetString(reader, "StreetNo"),
                StreetName = GetString(reader, "StreetName"),
                City = GetString(reader, "City"),
                County = GetString(reader, "County"),
                StateCode = GetString(reader, "StateCode"),
                ZipCode = GetString(reader, "ZipCode"),

                Latitude = GetDecimal(reader, "Latitude"),
                Longitude = GetDecimal(reader, "Longitude")
            };

            var historyLookup = await GetRecentSiteHistoryLookupAsync(
                new[] { row.SiteId },
                cancellationToken);

            if (historyLookup.TryGetValue(row.SiteId, out var history))
            {
                row.History.AddRange(history);
            }

            return row;
        }

        //Helpers
        private static string? GetDbString(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).Trim();
        }

        private static DateTime? GetNullableDateTime(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static long GetInt64(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt64(reader.GetValue(ordinal));
        }

        private static string? GetString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).Trim();
        }

        private static decimal? GetDecimal(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static int? GetNullableInt32(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static string? ResolveDashboardKind(SiteDashboardRouteInfo route)
        {
            var siteType = (route.SiteType ?? "").Trim().ToUpperInvariant();
            var siteId = (route.SiteId ?? "").Trim().ToUpperInvariant();

            if (siteType.Contains("AMS") || siteId.EndsWith("MR"))
            {
                return "ams-mr";
            }

            if (siteType.Contains("DACS") || siteId.StartsWith("DAC"))
            {
                return "dacs";
            }

            if (siteType.Contains("IG") || siteId.StartsWith("G"))
            {
                return "igsd";
            }

            return null;
        }
    }
}