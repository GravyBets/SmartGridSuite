using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<AmsSiteDashboardRow?> GetAmsMrSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = _parentDatabaseConnectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken);

            var sql = """
            SELECT
                a.SiteId,
                d.CodeValue     AS SiteStatus,
                dt.CodeValue    AS SiteType,
                cfg.Config      AS SiteConfigName,
                cfg.Comm1Type   AS PrimaryCommType,
                cfg.Comm2Type   AS SecondaryCommType,
                cfg.Descr       AS SiteConfigDescription,

                a.RadioSN       AS PrimaryCommsIdentifier,
                a.RadioIP       AS PrimaryCommsIp,
                a.EthernetIP    AS SecondaryLanIp,
                l.IP1           AS SecondaryWanIp,
                a.iTron_CR_Num  AS SecondaryCommsIdentifier,
                p.UserName      AS SecondaryCommsUsername,
                p.wifiSSID      AS SecondaryCommsSsid,
                p.CAMPassword   AS SecondaryCommsPassword,
                p.ATTSlot1      AS SecondarySimNumber,

                ant.SN          AS AntennaSerialNumber,
                enc.SN          AS EnclosureSerialNumber,
                enc.Model       AS EnclosureModel,

                tn.Descr        AS TopDescription,
                tn.TopName      AS TopName,
                ts.Sector       AS TopSector,
                ts.VIP          AS TopVip,
                ts.IPa          AS TopIpA,
                ts.IPb          AS TopIpB,


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

            var row = new AmsSiteDashboardRow
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
                TopVip = GetString(reader, "TopVip"),
                TopIpA = GetString(reader, "TopIpA"),
                TopIpB = GetString(reader, "TopIpB"),

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