using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<TowerDashboardRow?> GetTowerAsync(
            int topNameId, CancellationToken cancellationToken = default)
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
                    tn.Note          AS Note
                FROM [sgc_tnp].[TopName] tn
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
                Note = GetString(headerReader, "Note")
            };

            await headerReader.CloseAsync();

            var sectorSql = """
                SELECT
                    ts.id                         AS TopSiteId,
                    ts.Sector                     AS Sector,
                    ts.Tx_W                       AS TxWatts,
                    ts.TX_dbm                     AS TxDbm,
                    ts.ERP_W                      AS ErpWatts,
                    ts.Downtilt                   AS Downtilt,
                    ts.Channel                    AS Channel,
                    ts.Network                    AS Network,
                    ts.VIP                        AS Vip,
                    ts.IPa                        AS IPa,
                    ts.IPb                        AS IPb,
                    ts.VLAN                       AS Vlan,
                    CAST(ts.BSID AS varchar(50))  AS Bsid,
                    ts.AP_Model                   AS ApModel,
                    CAST(ts.AP_Freq AS varchar(50)) AS ApFrequency,
                    ts.AP_SNa                     AS AntennaSerialA,
                    ts.AP_SNb                     AS AntennaSerialB
                FROM [sgc_tnp].[TopSite] ts
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

                    TxWatts = GetDecimal(sectorReader, "TxWatts"),
                    TxDbm = GetDecimal(sectorReader, "TxDbm"),
                    ErpWatts = GetDecimal(sectorReader, "ErpWatts"),
                    Downtilt = GetDecimal(sectorReader, "Downtilt"),

                    Channel = GetNullableInt32(sectorReader, "Channel"),
                    Network = GetString(sectorReader, "Network"),

                    Vip = GetString(sectorReader, "Vip"),
                    IPa = GetString(sectorReader, "IPa"),
                    IPb = GetString(sectorReader, "IPb"),
                    Vlan = GetString(sectorReader, "Vlan"),

                    Bsid = GetString(sectorReader, "Bsid"),
                    ApModel = GetString(sectorReader, "ApModel"),
                    ApFrequency = GetString(sectorReader, "ApFrequency"),

                    AntennaSerialA = GetString(sectorReader, "AntennaSerialA"),
                    AntennaSerialB = GetString(sectorReader, "AntennaSerialB")

                    // Height / TestedHeight / Bearing / HighMount left null for now
                });
            }

            return row;
        }

        public async Task<List<TowerSearchRow>> SearchTowersAsync(
            string term, int take = 25, CancellationToken cancellationToken = default)
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
    }
}