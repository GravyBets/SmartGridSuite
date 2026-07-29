using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        private const int CacheSnapshotCommandTimeoutSeconds = 300;

        public async Task<ParentCacheSnapshot> GetCacheSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            return new ParentCacheSnapshot
            {
                AmsSites = await ReadAmsCacheRowsAsync(
                    conn,
                    cancellationToken),

                DacsSites = await ReadDacsCacheRowsAsync(
                    conn,
                    cancellationToken),

                IgsdSites = await ReadIgsdCacheRowsAsync(
                    conn,
                    cancellationToken),

                RxSites = await ReadRxCacheRowsAsync(
                    conn,
                    cancellationToken),

                Towers = await ReadTowerCacheRowsAsync(
                    conn,
                    cancellationToken)
            };
        }

        private static async Task<List<AmsSiteDashboardRow>>
            ReadAmsCacheRowsAsync(
                SqlConnection conn,
                CancellationToken cancellationToken)
        {
            const string sql = """
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
                ORDER BY a.SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn)
            {
                CommandTimeout = CacheSnapshotCommandTimeoutSeconds
            };

            await using var reader =
                await cmd.ExecuteReaderAsync(cancellationToken);

            var rows = new Dictionary<string, AmsSiteDashboardRow>(
                StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = GetString(reader, "SiteId") ?? "";

                if (string.IsNullOrWhiteSpace(siteId) ||
                    rows.ContainsKey(siteId))
                {
                    continue;
                }

                rows[siteId] = new AmsSiteDashboardRow
                {
                    SiteId = siteId,
                    SiteStatus = GetString(reader, "SiteStatus"),
                    SiteType = GetString(reader, "SiteType"),
                    SiteConfigName =
                        GetString(reader, "SiteConfigName"),
                    PrimaryCommType =
                        GetString(reader, "PrimaryCommType"),
                    SecondaryCommType =
                        GetString(reader, "SecondaryCommType"),
                    SiteConfigDescription =
                        GetString(reader, "SiteConfigDescription"),

                    PrimaryCommsIdentifier =
                        GetString(reader, "PrimaryCommsIdentifier"),
                    PrimaryCommsIp =
                        GetString(reader, "PrimaryCommsIp"),

                    SecondaryLanIp =
                        GetString(reader, "SecondaryLanIp"),
                    SecondaryWanIp =
                        GetString(reader, "SecondaryWanIp"),

                    SecondaryCommsIdentifier =
                        GetString(reader, "SecondaryCommsIdentifier"),
                    SecondaryCommsUsername =
                        GetString(reader, "SecondaryCommsUsername"),
                    SecondaryCommsSsid =
                        GetString(reader, "SecondaryCommsSsid"),
                    SecondaryCommsPassword =
                        GetString(reader, "SecondaryCommsPassword"),
                    SecondarySimNumber =
                        GetString(reader, "SecondarySimNumber"),

                    AntennaSerialNumber =
                        GetString(reader, "AntennaSerialNumber"),
                    EnclosureSerialNumber =
                        GetString(reader, "EnclosureSerialNumber"),
                    EnclosureModel =
                        GetString(reader, "EnclosureModel"),

                    TopName = GetString(reader, "TopName"),
                    TopDescription =
                        GetString(reader, "TopDescription"),
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
            }

            return rows.Values
                .OrderBy(x => x.SiteId)
                .ToList();
        }

        private static async Task<List<DacsSiteDashboardRow>>
            ReadDacsCacheRowsAsync(
                SqlConnection conn,
                CancellationToken cancellationToken)
        {
            const string sql = """
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

                    tn.Descr            AS TopDescription,
                    tn.TopName          AS TopName,
                    ts.Sector           AS TopSector,
                    ts.VIP              AS TopVip,
                    ts.IPa              AS TopIpA,
                    ts.IPb              AS TopIpB,

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
                WHERE dt.CodeValue LIKE '%DACS%'
                ORDER BY s.SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn)
            {
                CommandTimeout = CacheSnapshotCommandTimeoutSeconds
            };

            await using var reader =
                await cmd.ExecuteReaderAsync(cancellationToken);

            var rows = new Dictionary<string, DacsSiteDashboardRow>(
                StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = GetString(reader, "SiteId") ?? "";

                if (string.IsNullOrWhiteSpace(siteId) ||
                    rows.ContainsKey(siteId))
                {
                    continue;
                }

                rows[siteId] = new DacsSiteDashboardRow
                {
                    SiteId = siteId,
                    SiteStatus = GetString(reader, "SiteStatus"),
                    SiteType = GetString(reader, "SiteType"),
                    SiteConfigName =
                        GetString(reader, "SiteConfigName"),
                    PrimaryCommType =
                        GetString(reader, "PrimaryCommType"),
                    SecondaryCommType =
                        GetString(reader, "SecondaryCommType"),
                    SiteConfigDescription =
                        GetString(reader, "SiteConfigDescription"),

                    PrimaryCommsIp =
                        GetString(reader, "PrimaryCommsIp"),
                    TunnelIp = GetString(reader, "TunnelIp"),
                    RtuIp = GetString(reader, "RtuIp"),

                    TopName = GetString(reader, "TopName"),
                    TopDescription =
                        GetString(reader, "TopDescription"),
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
            }

            return rows.Values
                .OrderBy(x => x.SiteId)
                .ToList();
        }

        private static async Task<List<IgsdSiteDashboardRow>>
            ReadIgsdCacheRowsAsync(
                SqlConnection conn,
                CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT
                    i.SiteId,
                    d.CodeValue        AS SiteStatus,
                    dt.CodeValue       AS SiteType,
                    cfg.Config         AS SiteConfigName,
                    cfg.Comm1Type      AS PrimaryCommType,
                    cfg.Comm2Type      AS SecondaryCommType,
                    cfg.Descr          AS SiteConfigDescription,

                    CASE
                        WHEN UPPER(
                            LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN NULL
                        ELSE i.RadioSN
                    END                AS PrimaryCommsIdentifier,

                    CASE
                        WHEN UPPER(
                            LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN NULL
                        ELSE i.RadioIP
                    END                AS PrimaryCommsIp,

                    i.PriProtLanDigi   AS PrimaryLanIp,

                    CASE
                        WHEN UPPER(
                            LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
                          OR LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = ''
                        THEN i.PriDigiWanOut
                        ELSE i.PriWanOut
                    END                AS PrimaryWanIp,

                    CASE
                        WHEN UPPER(
                            LTRIM(RTRIM(ISNULL(i.RadioIP, '')))) = 'NA'
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

                    tn.Descr           AS TopDescription,
                    tn.TopName         AS TopName,
                    ts.Sector          AS TopSector,
                    ts.VIP             AS TopVip,
                    ts.IPa             AS TopIpA,
                    ts.IPb             AS TopIpB,

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
                ORDER BY i.SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn)
            {
                CommandTimeout = CacheSnapshotCommandTimeoutSeconds
            };

            await using var reader =
                await cmd.ExecuteReaderAsync(cancellationToken);

            var rows = new Dictionary<string, IgsdSiteDashboardRow>(
                StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = GetString(reader, "SiteId") ?? "";

                if (string.IsNullOrWhiteSpace(siteId) ||
                    rows.ContainsKey(siteId))
                {
                    continue;
                }

                rows[siteId] = new IgsdSiteDashboardRow
                {
                    SiteId = siteId,
                    SiteStatus = GetString(reader, "SiteStatus"),
                    SiteType = GetString(reader, "SiteType"),
                    SiteConfigName =
                        GetString(reader, "SiteConfigName"),
                    PrimaryCommType =
                        GetString(reader, "PrimaryCommType"),
                    SecondaryCommType =
                        GetString(reader, "SecondaryCommType"),
                    SiteConfigDescription =
                        GetString(reader, "SiteConfigDescription"),

                    PrimaryCommsIdentifier =
                        GetString(reader, "PrimaryCommsIdentifier"),
                    PrimaryCommsIp =
                        GetString(reader, "PrimaryCommsIp"),
                    PrimaryLanIp =
                        GetString(reader, "PrimaryLanIp"),
                    PrimaryWanIp =
                        GetString(reader, "PrimaryWanIp"),
                    PrimaryTunnelIp =
                        GetString(reader, "PrimaryTunnelIp"),
                    PrimaryRtuIp =
                        GetString(reader, "PrimaryRtuIp"),

                    SecondaryCommsIdentifier =
                        GetString(reader, "SecondaryCommsIdentifier"),
                    SecondaryWanIp =
                        GetString(reader, "SecondaryWanIp"),
                    SecondaryLanIp =
                        GetString(reader, "SecondaryLanIp"),
                    SecondaryTunnelIp =
                        GetString(reader, "SecondaryTunnelIp"),
                    SecondaryRtuIp =
                        GetString(reader, "SecondaryRtuIp"),

                    AntennaSerialNumber =
                        GetString(reader, "AntennaSerialNumber"),
                    EnclosureSerialNumber =
                        GetString(reader, "EnclosureSerialNumber"),
                    EnclosureModel =
                        GetString(reader, "EnclosureModel"),
                    CyberlockSerialNumber =
                        GetString(reader, "CyberlockSerialNumber"),
                    TunnelPsk =
                        GetString(reader, "TunnelPsk"),

                    TopName = GetString(reader, "TopName"),
                    TopDescription =
                        GetString(reader, "TopDescription"),
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
            }

            return rows.Values
                .OrderBy(x => x.SiteId)
                .ToList();
        }

        private static async Task<List<RxSiteDashboardRow>>
            ReadRxCacheRowsAsync(
                SqlConnection conn,
                CancellationToken cancellationToken)
        {
            const string sql = """
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
                ORDER BY r.SiteId;
                """;

            await using var cmd = new SqlCommand(sql, conn)
            {
                CommandTimeout = CacheSnapshotCommandTimeoutSeconds
            };

            await using var reader =
                await cmd.ExecuteReaderAsync(cancellationToken);

            var rows = new Dictionary<string, RxSiteDashboardRow>(
                StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = GetString(reader, "SiteId") ?? "";

                if (string.IsNullOrWhiteSpace(siteId) ||
                    rows.ContainsKey(siteId))
                {
                    continue;
                }

                rows[siteId] = new RxSiteDashboardRow
                {
                    SiteId = siteId,
                    SiteStatus = GetString(reader, "SiteStatus"),
                    SiteType = GetString(reader, "SiteType"),
                    SiteConfigName =
                        GetString(reader, "SiteConfigName"),
                    SiteConfigDescription =
                        GetString(reader, "SiteConfigDescription"),

                    MeterNumber =
                        GetString(reader, "MeterNumber"),
                    MacAddress =
                        GetString(reader, "MacAddress"),
                    PolePoint =
                        GetString(reader, "PolePoint"),
                    TransformerGln =
                        GetString(reader, "TransformerGln"),

                    StreetNo = GetString(reader, "StreetNo"),
                    StreetName = GetString(reader, "StreetName"),
                    City = GetString(reader, "City"),
                    County = GetString(reader, "County"),
                    StateCode = GetString(reader, "StateCode"),
                    ZipCode = GetString(reader, "ZipCode"),

                    Latitude = GetDecimal(reader, "Latitude"),
                    Longitude = GetDecimal(reader, "Longitude")
                };
            }

            return rows.Values
                .OrderBy(x => x.SiteId)
                .ToList();
        }

        private static async Task<List<TowerDashboardRow>>
            ReadTowerCacheRowsAsync(
                SqlConnection conn,
                CancellationToken cancellationToken)
        {
            const string headerSql = """
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
                    CAST(gps.Latitude AS decimal(18, 8))
                                      AS Latitude,
                    CAST(gps.Longitude AS decimal(18, 8))
                                      AS Longitude,

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
                ORDER BY tn.id;
                """;

            var towers = new Dictionary<int, TowerDashboardRow>();

            await using (var headerCmd = new SqlCommand(headerSql, conn)
            {
                CommandTimeout = CacheSnapshotCommandTimeoutSeconds
            })
            await using (var headerReader =
                await headerCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await headerReader.ReadAsync(cancellationToken))
                {
                    var topNameId =
                        GetNullableInt32(headerReader, "TopNameId") ?? 0;

                    if (topNameId <= 0 ||
                        towers.ContainsKey(topNameId))
                    {
                        continue;
                    }

                    var customerOwnedValue =
                        GetNullableInt32(
                            headerReader,
                            "CustomerOwnedValue");

                    var streetNo =
                        GetString(headerReader, "StreetNo");
                    var streetName =
                        GetString(headerReader, "StreetName");
                    var city =
                        GetString(headerReader, "City");
                    var stateCode =
                        GetString(headerReader, "StateCode");
                    var zipCode =
                        GetString(headerReader, "ZipCode");

                    towers[topNameId] = new TowerDashboardRow
                    {
                        TopNameId = topNameId,
                        TopName =
                            GetString(headerReader, "TopName"),
                        TopType =
                            GetString(headerReader, "TopType"),
                        TopDescription =
                            GetString(
                                headerReader,
                                "TopDescription"),
                        IpAssignment =
                            GetString(
                                headerReader,
                                "IpAssignment"),
                        GpsId =
                            GetNullableInt32(
                                headerReader,
                                "GpsId"),
                        CnpAreaId =
                            GetNullableInt32(
                                headerReader,
                                "CnpAreaId"),
                        CustomerOwned =
                            customerOwnedValue.HasValue
                                ? customerOwnedValue.Value != 0
                                : null,
                        Note =
                            GetString(headerReader, "Note"),

                        HistorySiteId =
                            GetString(
                                headerReader,
                                "HistorySiteId"),

                        Latitude =
                            GetDecimal(
                                headerReader,
                                "Latitude"),
                        Longitude =
                            GetDecimal(
                                headerReader,
                                "Longitude"),

                        StreetNo = streetNo,
                        StreetName = streetName,
                        City = city,
                        County =
                            GetString(headerReader, "County"),
                        StateCode = stateCode,
                        ZipCode = zipCode,
                        FullAddress = BuildTowerFullAddress(
                            streetNo,
                            streetName,
                            city,
                            stateCode,
                            zipCode)
                    };
                }
            }

            const string sectorSql = """
                SELECT
                    ts.TopName_id                  AS TopNameId,
                    ts.id                          AS TopSiteId,
                    ts.Sector                      AS Sector,
                    ts.TX_dbm                      AS TxDbm,
                    ts.Downtilt                    AS Downtilt,
                    ts.Channel                     AS Channel,
                    ch.TX_Freq                     AS ChannelTxFrequency,
                    ch.RX_Freq                     AS ChannelRxFrequency,
                    ts.Network                     AS Network,
                    ts.VIP                         AS Vip,
                    ts.IPa                         AS IPa,
                    ts.IPb                         AS IPb,
                    ts.VLAN                        AS Vlan,
                    CAST(ts.BSID AS varchar(50))   AS Bsid,
                    ts.AP_SNa                      AS AntennaSerialA,
                    ts.AP_SNb                      AS AntennaSerialB

                FROM [sgc_tnp].[TopSite] ts
                LEFT JOIN [cnpamscr].[700Channel_T] ch
                    ON ts.Channel = ch.Channel
                ORDER BY ts.TopName_id, ts.Sector, ts.id;
                """;

            await using (var sectorCmd = new SqlCommand(sectorSql, conn)
            {
                CommandTimeout = CacheSnapshotCommandTimeoutSeconds
            })
            await using (var sectorReader =
                await sectorCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await sectorReader.ReadAsync(cancellationToken))
                {
                    var topNameId =
                        GetNullableInt32(
                            sectorReader,
                            "TopNameId") ?? 0;

                    if (!towers.TryGetValue(
                        topNameId,
                        out var tower))
                    {
                        continue;
                    }

                    tower.Sectors.Add(new TowerSectorRow
                    {
                        TopSiteId =
                            GetNullableInt32(
                                sectorReader,
                                "TopSiteId") ?? 0,
                        Sector =
                            GetString(
                                sectorReader,
                                "Sector"),

                        TxDbm =
                            GetDecimal(
                                sectorReader,
                                "TxDbm"),
                        Downtilt =
                            GetDecimal(
                                sectorReader,
                                "Downtilt"),

                        Channel =
                            GetNullableInt32(
                                sectorReader,
                                "Channel"),
                        ChannelTxFrequency =
                            GetDecimal(
                                sectorReader,
                                "ChannelTxFrequency"),
                        ChannelRxFrequency =
                            GetDecimal(
                                sectorReader,
                                "ChannelRxFrequency"),

                        Network =
                            GetString(
                                sectorReader,
                                "Network"),

                        Vip =
                            GetString(
                                sectorReader,
                                "Vip"),
                        IPa =
                            GetString(
                                sectorReader,
                                "IPa"),
                        IPb =
                            GetString(
                                sectorReader,
                                "IPb"),
                        Vlan =
                            GetString(
                                sectorReader,
                                "Vlan"),

                        Bsid =
                            GetString(
                                sectorReader,
                                "Bsid"),

                        AntennaSerialA =
                            GetString(
                                sectorReader,
                                "AntennaSerialA"),
                        AntennaSerialB =
                            GetString(
                                sectorReader,
                                "AntennaSerialB")
                    });
                }
            }

            return towers.Values
                .OrderBy(x => x.TopName)
                .ThenBy(x => x.TopNameId)
                .ToList();
        }
    }
}