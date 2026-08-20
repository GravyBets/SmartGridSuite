using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<RxSiteDashboardRow?> GetRxSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = _parentDatabaseConnectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken);

            var sql = """
            SELECT
                r.SiteId,
                d.CodeValue      AS SiteStatus,
                dt.CodeValue     AS SiteType,
                cfg.Config       AS SiteConfigName,
                cfg.Descr        AS SiteConfigDescription,

                r.MeterNumber    AS MeterNumber,
                r.MACAddress     AS MacAddress,
                r.PolePoint      AS PolePoint,
                r.TransfGLN      AS TransformerGln,

                addr.StreetNo,
                addr.StreetName,
                addr.City,
                addr.County,
                addr.StateCode,
                addr.ZipCode,

                gps.Latitude,
                gps.Longitude
            FROM [sgc_comm].[RE] r
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

            var row = new RxSiteDashboardRow
            {
                SiteId = GetString(reader, "SiteId") ?? "",

                SiteStatus = GetString(reader, "SiteStatus"),
                SiteType = GetString(reader, "SiteType"),
                SiteConfigName = GetString(reader, "SiteConfigName"),                
                SiteConfigDescription = GetString(reader, "SiteConfigDescription"),

                MeterNumber = GetString(reader, "MeterNumber"),
                MacAddress = GetString(reader, "MacAddress"),
                PolePoint = GetString(reader, "PolePoint"),
                TransformerGln = GetString(reader, "TransformerGln"),

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
                new[] { row.SiteId }, cancellationToken);

            if (historyLookup.TryGetValue(row.SiteId, out var history))
            {
                row.History.AddRange(history);
            }

            return row;
        }
    }
}