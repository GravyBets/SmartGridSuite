using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<TowerDashboardRow?> GetTowerAsync(int topNameId, CancellationToken cancellationToken = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var headerSql = """
                SELECT
                    tn.id            AS TopNameId,
                    tn.TopName       AS TopName,
                    tn.TopType       AS TopType,
                    tn.Descr         AS TopDescription,
                    tn.IP_Assignment AS IpAssignment,
                    tn.GPS_id        AS GpsId,
                    tn.CNP_Area_id   AS CnpAreaId,
                    tn.CustomerOwned AS CustomerOwnedValue,
                    tn.Note          AS Note,

                    gps.SiteId       AS HistorySiteId,
                    CAST(gps.Latitude AS decimal(18, 8))  AS Latitude,
                    CAST(gps.Longitude AS decimal(18, 8)) AS Longitude,

                    addr.StreetNo    AS StreetNo,
                    addr.StreetName  AS StreetName,
                    addr.City        AS City,
                    addr.County      AS County,
                    addr.StateCode   AS StateCode,
                    addr.ZipCode     AS ZipCode
                FROM [sgc_tnp].[TopName] tn
                LEFT JOIN [sgc_main].[GPS] gps
                    ON tn.GPS_id = gps.id
                LEFT JOIN [sgc_main].[Address] addr
                    ON gps.SiteId = addr.SiteId
                WHERE tn.id = @TopNameId;
                """;

            await using var headerCmd = new SqlCommand(headerSql, conn);
            headerCmd.Parameters.Add(new SqlParameter("@TopNameId", SqlDbType.Int) { Value = topNameId });

            await using var headerReader = await headerCmd.ExecuteReaderAsync(cancellationToken);

            if (!await headerReader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var customerOwnedValue = GetNullableInt32(headerReader, "CustomerOwnedValue");

            var streetNo = GetString(headerReader, "StreetNo");
            var streetName = GetString(headerReader, "StreetName");
            var city = GetString(headerReader, "City");
            var stateCode = GetString(headerReader, "StateCode");
            var zipCode = GetString(headerReader, "ZipCode");
            var historySiteId = GetString(headerReader, "HistorySiteId");

            var row = new TowerDashboardRow
            {
                TopNameId = GetNullableInt32(headerReader, "TopNameId") ?? 0,
                TopName = GetString(headerReader, "TopName"),
                TopType = GetString(headerReader, "TopType"),
                TopDescription = GetString(headerReader, "TopDescription"),
                IpAssignment = GetString(headerReader, "IpAssignment"),
                GpsId = GetNullableInt32(headerReader, "GpsId"),
                CnpAreaId = GetNullableInt32(headerReader, "CnpAreaId"),
                CustomerOwned = customerOwnedValue.HasValue ? customerOwnedValue.Value != 0 : null,
                Note = GetString(headerReader, "Note"),

                HistorySiteId = historySiteId,

                Latitude = GetDecimal(headerReader, "Latitude"),
                Longitude = GetDecimal(headerReader, "Longitude"),

                StreetNo = streetNo,
                StreetName = streetName,
                City = city,
                County = GetString(headerReader, "County"),
                StateCode = stateCode,
                ZipCode = zipCode,
                FullAddress = BuildTowerFullAddress(streetNo, streetName, city, stateCode, zipCode)
            };

            await headerReader.CloseAsync();

            var historyCandidates = new[]
            {
                row.HistorySiteId,
                row.TopName,
                row.TopName?.Replace("_", "-"),
                row.TopName?.Replace("-", "_"),
                row.TopName?.Replace("_", ""),
                row.TopName?.Replace("-", "")
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            if (historyCandidates.Length > 0)
            {
                var historyLookup = await GetRecentSiteHistoryLookupAsync(
                    historyCandidates,
                    cancellationToken);

                foreach (var candidate in historyCandidates)
                {
                    if (historyLookup.TryGetValue(candidate, out var history))
                    {
                        row.History.AddRange(history);
                        break;
                    }
                }
            }

            var sectorSql = """
                SELECT
                    ts.id                           AS TopSiteId,
                    ts.Sector                       AS Sector,
                    ts.TX_dbm                       AS TxDbm,
                    ts.Downtilt                     AS Downtilt,
                    ts.Channel                      AS Channel,
                    ch.TX_Freq                      AS ChannelTxFrequency,
                    ch.RX_Freq                      AS ChannelRxFrequency,
                    ts.Network                      AS Network,
                    ts.VIP                          AS Vip,
                    ts.IPa                          AS IPa,
                    ts.IPb                          AS IPb,
                    ts.VLAN                         AS Vlan,
                    CAST(ts.BSID AS varchar(50))    AS Bsid,
                    ts.AP_SNa                       AS AntennaSerialA,
                    ts.AP_SNb                       AS AntennaSerialB
                FROM [sgc_tnp].[TopSite] ts
                LEFT JOIN [cnpamscr].[700Channel_T] ch
                    ON ts.Channel = ch.Channel
                WHERE ts.TopName_id = @TopNameId
                ORDER BY ts.Sector, ts.id;
                """;

            await using var sectorCmd = new SqlCommand(sectorSql, conn);
            sectorCmd.Parameters.Add(new SqlParameter("@TopNameId", SqlDbType.Int) { Value = topNameId });

            await using var sectorReader = await sectorCmd.ExecuteReaderAsync(cancellationToken);

            while (await sectorReader.ReadAsync(cancellationToken))
            {
                row.Sectors.Add(new TowerSectorRow
                {
                    TopSiteId = GetNullableInt32(sectorReader, "TopSiteId") ?? 0,
                    Sector = GetString(sectorReader, "Sector"),

                    TxDbm = GetDecimal(sectorReader, "TxDbm"),
                    Downtilt = GetDecimal(sectorReader, "Downtilt"),

                    Channel = GetNullableInt32(sectorReader, "Channel"),
                    ChannelTxFrequency = GetDecimal(sectorReader, "ChannelTxFrequency"),
                    ChannelRxFrequency = GetDecimal(sectorReader, "ChannelRxFrequency"),

                    Network = GetString(sectorReader, "Network"),

                    Vip = GetString(sectorReader, "Vip"),
                    IPa = GetString(sectorReader, "IPa"),
                    IPb = GetString(sectorReader, "IPb"),
                    Vlan = GetString(sectorReader, "Vlan"),

                    Bsid = GetString(sectorReader, "Bsid"),

                    AntennaSerialA = GetString(sectorReader, "AntennaSerialA"),
                    AntennaSerialB = GetString(sectorReader, "AntennaSerialB")

                    // Height / TestedHeight / Bearing / HighMount left null for now
                });
            }

            return row;
        }

        public async Task<List<TowerSearchRow>> SearchTowersAsync(string term, int take = 25, CancellationToken cancellationToken = default)
        {
            term = (term ?? "").Trim();

            if (string.IsNullOrWhiteSpace(term))
            {
                return new List<TowerSearchRow>();
            }

            if (take <= 0) take = 25;
            if (take > 100) take = 100;

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
                SELECT TOP (@Take)
                    tn.id        AS TopNameId,
                    tn.TopName   AS TopName,
                    tn.TopType   AS TopType,
                    tn.Descr     AS TopDescription,
                    COUNT(ts.id) AS SectorCount
                FROM [sgc_tnp].[TopName] tn
                LEFT JOIN [sgc_tnp].[TopSite] ts
                    ON ts.TopName_id = tn.id
                WHERE
                    tn.TopName LIKE @LikeTerm
                    OR tn.Descr LIKE @LikeTerm
                    OR REPLACE(tn.TopName, '_', '-') LIKE REPLACE(@LikeTerm, '_', '-')
                    OR REPLACE(tn.TopName, '-', '_') LIKE REPLACE(@LikeTerm, '-', '_')
                    OR REPLACE(tn.Descr, '_', '-') LIKE REPLACE(@LikeTerm, '_', '-')
                    OR REPLACE(tn.Descr, '-', '_') LIKE REPLACE(@LikeTerm, '-', '_')
                GROUP BY
                    tn.id,
                    tn.TopName,
                    tn.TopType,
                    tn.Descr
                ORDER BY
                    tn.TopName;
                """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@Take", SqlDbType.Int) { Value = take });
            cmd.Parameters.Add(new SqlParameter("@LikeTerm", SqlDbType.NVarChar, 100) { Value = $"%{term}%" });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var results = new List<TowerSearchRow>();

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new TowerSearchRow
                {
                    TopNameId = GetNullableInt32(reader, "TopNameId") ?? 0,
                    TopName = GetString(reader, "TopName"),
                    TopType = GetString(reader, "TopType"),
                    TopDescription = GetString(reader, "TopDescription"),
                    SectorCount = GetNullableInt32(reader, "SectorCount") ?? 0
                });
            }

            return results;
        }

        private static string? BuildTowerFullAddress(string? streetNo, string? streetName, string? city, string? stateCode, string? zipCode)
        {
            var line1 = string.Join(" ", new[] { streetNo, streetName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var cityState = string.Join(", ", new[] { city, stateCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var line2 = string.Join(" ", new[] { cityState, zipCode }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var combined = string.Join("  ", new[] { line1, line2 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrWhiteSpace(combined)
                ? null
                : combined;
        }
    }
}