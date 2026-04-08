using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<DacsSiteDashboardRow?> GetDacsSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
                SELECT
                    s.SiteId,
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

                FROM [sgc_main].[Site] s
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
                    ON s.SiteId = addr.SiteId
                LEFT JOIN [sgc_main].[GPS] gps
                    ON s.SiteId = gps.SiteId
                LEFT JOIN [sgc_equip].[Radio700] r
                    ON r.SiteId = s.SiteId
                WHERE s.SiteId = @SiteId
                  AND dt.CodeValue LIKE '%DACS%';
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
    }
}