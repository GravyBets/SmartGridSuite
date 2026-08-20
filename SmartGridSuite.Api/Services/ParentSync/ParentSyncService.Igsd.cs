using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<IgsdSiteDashboardRow?> GetIgsdSiteAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = _parentDatabaseConnectionFactory.CreateConnection();
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