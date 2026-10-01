using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Contracts.Dispatcher;
using System.Data;

namespace SmartGridSuite.Api.Services
{
    public sealed class DeviceLookupService
    {
        private const int ParentCommandTimeoutSeconds = 45;
        private const int MaxParentRowsPerSource = 50;
        private const int MaxSmartGridRows = 250;

        private readonly SmartGridDbContext _db;
        private readonly ParentDatabaseConnectionFactory _parentDatabaseConnectionFactory;

        public DeviceLookupService(
            SmartGridDbContext db,
            ParentDatabaseConnectionFactory parentDatabaseConnectionFactory)
        {
            _db = db;
            _parentDatabaseConnectionFactory = parentDatabaseConnectionFactory;
        }

        public async Task<DeviceLookupResponseDto> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            query = (query ?? string.Empty).Trim();

            var response = new DeviceLookupResponseDto
            {
                Query = query
            };

            if (string.IsNullOrWhiteSpace(query))
                return response;

            try
            {
                await LoadParentDatabaseRecordsAsync(
                    response,
                    query,
                    cancellationToken);
            }
            catch (Exception ex) when (
                ex is SqlException ||
                ex is InvalidOperationException ||
                ex is TimeoutException)
            {
                /*
                 * Device Lookup is also useful for historical SmartGridSuite
                 * records. If Parent DB is temporarily unavailable, keep those
                 * results instead of failing the whole search.
                 */
                response.Warning =
                    $"Parent DB device search failed: {ex.Message}";
            }

            await LoadSmartGridSuiteRecordsAsync(
                response,
                query,
                cancellationToken);

            response.RelatedSiteIds =
                response.RelatedSiteIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            return response;
        }

        private async Task LoadParentDatabaseRecordsAsync(
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            await using var connection =
                _parentDatabaseConnectionFactory.CreateConnection();

            await connection.OpenAsync(cancellationToken);

            await ReadPmrMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadLteMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadAmsMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadIgsdMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadRadio700MatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadRangeExtenderMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadAntennaMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            await ReadEnclosureMatchesAsync(
                connection,
                response,
                query,
                cancellationToken);

            var parentSiteIds =
                response.ParentRecords
                    .Select(x => x.SiteId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            /*
             * Also try the raw query as a SiteId. This makes direct site
             * searches useful even when no communications/equipment row
             * happens to match.
             */
            parentSiteIds.Add(query);

            await ReadSiteMetadataAsync(
                connection,
                response,
                parentSiteIds,
                cancellationToken);
        }

        private async Task ReadPmrMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    p.SN,
                    p.ATTSlot1,
                    p.VzwSlot2,
                    p.UserName,
                    p.wifiSSID,
                    p.CAMPassword,
                    a.SiteId,
                    a.RadioSN,
                    a.RadioIP,
                    a.EthernetIP,
                    l.IP1
                FROM [sgc_equip].[PMR] p
                LEFT JOIN [sgc_comm].[AMS] a
                    ON LTRIM(RTRIM(CONVERT(nvarchar(150), a.iTron_CR_Num))) =
                       LTRIM(RTRIM(CONVERT(nvarchar(150), p.SN)))
                LEFT JOIN [sgc_equip].[LTE] l
                    ON a.SiteId = l.SiteId
                WHERE
                    LTRIM(RTRIM(CONVERT(nvarchar(150), p.SN))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), p.ATTSlot1))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), p.VzwSlot2))) = @Query OR
                    LTRIM(RTRIM(ISNULL(a.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), a.RadioSN))) = @Query OR
                    LTRIM(RTRIM(ISNULL(a.RadioIP, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(a.EthernetIP, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(l.IP1, ''))) = @Query;
                """;

            await using var command =
                CreateParentCommand(
                    connection,
                    sql,
                    query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var serial = ReadText(reader, "SN");
                var attSim = ReadText(reader, "ATTSlot1");
                var vzwSim = ReadText(reader, "VzwSlot2");
                var siteId = ReadText(reader, "SiteId");
                var radioSn = ReadText(reader, "RadioSN");
                var radioIp = ReadText(reader, "RadioIP");
                var ethernetIp = ReadText(reader, "EthernetIP");
                var wanIp = ReadText(reader, "IP1");

                AddParentRecord(
                    response,
                    source: "Parent DB",
                    recordType: "PMR",
                    siteId,
                    DetermineMatchField(
                        query,
                        ("PMR SN", serial),
                        ("AT&T SIM", attSim),
                        ("Verizon SIM", vzwSim),
                        ("Site", siteId),
                        ("AMS Radio SN", radioSn),
                        ("AMS Radio IP", radioIp),
                        ("AMS Ethernet IP", ethernetIp),
                        ("LTE WAN IP", wanIp)),
                    ("PMR SN", serial),
                    ("AT&T SIM", attSim),
                    ("Verizon SIM", vzwSim),
                    ("Current Site", siteId),
                    ("AMS Radio SN", radioSn),
                    ("AMS Radio IP", radioIp),
                    ("AMS Ethernet IP", ethernetIp),
                    ("LTE WAN IP", wanIp),
                    ("PMR Username", ReadText(reader, "UserName")),
                    ("PMR WiFi SSID", ReadText(reader, "wifiSSID")),
                    ("PMR Password", ReadText(reader, "CAMPassword")));
            }
        }

        private async Task ReadLteMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    l.SiteId,
                    l.SN,
                    l.IP1
                FROM [sgc_equip].[LTE] l
                WHERE
                    LTRIM(RTRIM(ISNULL(l.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), l.SN))) = @Query OR
                    LTRIM(RTRIM(ISNULL(l.IP1, ''))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var serial = ReadText(reader, "SN");
                var ip = ReadText(reader, "IP1");

                AddParentRecord(
                    response,
                    "Parent DB",
                    "LTE",
                    siteId,
                    DetermineMatchField(
                        query,
                        ("Site", siteId),
                        ("LTE SN", serial),
                        ("LTE IP", ip)),
                    ("Site", siteId),
                    ("LTE SN", serial),
                    ("LTE IP", ip));
            }
        }

        private async Task ReadAmsMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    a.SiteId,
                    a.RadioSN,
                    a.RadioIP,
                    a.EthernetIP,
                    a.iTron_CR_Num
                FROM [sgc_comm].[AMS] a
                WHERE
                    LTRIM(RTRIM(ISNULL(a.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), a.RadioSN))) = @Query OR
                    LTRIM(RTRIM(ISNULL(a.RadioIP, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(a.EthernetIP, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), a.iTron_CR_Num))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var radioSn = ReadText(reader, "RadioSN");
                var radioIp = ReadText(reader, "RadioIP");
                var ethernetIp = ReadText(reader, "EthernetIP");
                var pmrSn = ReadText(reader, "iTron_CR_Num");

                AddParentRecord(
                    response,
                    "Parent DB",
                    "AMS / MR",
                    siteId,
                    DetermineMatchField(
                        query,
                        ("Site", siteId),
                        ("Radio SN", radioSn),
                        ("Radio IP", radioIp),
                        ("Ethernet IP", ethernetIp),
                        ("PMR SN", pmrSn)),
                    ("Site", siteId),
                    ("Radio SN", radioSn),
                    ("Radio IP", radioIp),
                    ("Ethernet IP", ethernetIp),
                    ("PMR SN", pmrSn));
            }
        }

        private async Task ReadIgsdMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    i.SiteId,
                    i.RadioSN,
                    i.RadioIP,
                    i.PriProtLanDigi,
                    i.PriWanOut,
                    i.PriDigiWanOut,
                    i.PriProtLanSubN,
                    i.PriProtLanRtu,
                    i.SecDigiWanOut,
                    i.SecProtLanDigi,
                    i.SecProtLanSubN,
                    i.SecProtLanRtu,
                    i.Cyberlock
                FROM [sgc_comm].[IGSD] i
                WHERE
                    LTRIM(RTRIM(ISNULL(i.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), i.RadioSN))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.RadioIP, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.PriProtLanDigi, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.PriWanOut, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.PriDigiWanOut, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.PriProtLanSubN, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.PriProtLanRtu, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.SecDigiWanOut, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.SecProtLanDigi, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.SecProtLanSubN, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(i.SecProtLanRtu, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), i.Cyberlock))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var fields = new (string Label, string Value)[]
                {
                    ("Site", siteId),
                    ("Radio SN", ReadText(reader, "RadioSN")),
                    ("Radio IP", ReadText(reader, "RadioIP")),
                    ("Primary LAN IP", ReadText(reader, "PriProtLanDigi")),
                    ("Primary WAN IP", ReadText(reader, "PriWanOut")),
                    ("Primary Digi WAN", ReadText(reader, "PriDigiWanOut")),
                    ("Primary Tunnel IP", ReadText(reader, "PriProtLanSubN")),
                    ("Primary RTU IP", ReadText(reader, "PriProtLanRtu")),
                    ("Secondary WAN IP", ReadText(reader, "SecDigiWanOut")),
                    ("Secondary LAN IP", ReadText(reader, "SecProtLanDigi")),
                    ("Secondary Tunnel IP", ReadText(reader, "SecProtLanSubN")),
                    ("Secondary RTU IP", ReadText(reader, "SecProtLanRtu")),
                    ("Cyberlock SN", ReadText(reader, "Cyberlock"))
                };

                AddParentRecord(
                    response,
                    "Parent DB",
                    "IGSD",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
        }

        private async Task ReadRadio700MatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    r.SiteId,
                    r.RadioIP,
                    r.RtuWanVLAN,
                    r.RtuWanVLANGateway
                FROM [sgc_equip].[Radio700] r
                WHERE
                    LTRIM(RTRIM(ISNULL(r.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(r.RadioIP, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(r.RtuWanVLAN, ''))) = @Query OR
                    LTRIM(RTRIM(ISNULL(r.RtuWanVLANGateway, ''))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var fields = new (string Label, string Value)[]
                {
                    ("Site", siteId),
                    ("Radio IP", ReadText(reader, "RadioIP")),
                    ("RTU WAN VLAN", ReadText(reader, "RtuWanVLAN")),
                    ("RTU WAN Gateway", ReadText(reader, "RtuWanVLANGateway"))
                };

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Radio700 / DACS",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
        }

        private async Task ReadRangeExtenderMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    r.SiteId,
                    r.MeterNumber,
                    r.MACAddress,
                    r.PolePoint,
                    r.TransfGLN
                FROM [sgc_comm].[RE] r
                WHERE
                    LTRIM(RTRIM(ISNULL(r.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), r.MeterNumber))) = @Query OR
                    LTRIM(RTRIM(ISNULL(r.MACAddress, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), r.PolePoint))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), r.TransfGLN))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var fields = new (string Label, string Value)[]
                {
                    ("Site", siteId),
                    ("RX / Meter SN", ReadText(reader, "MeterNumber")),
                    ("MAC Address", ReadText(reader, "MACAddress")),
                    ("Pole Point", ReadText(reader, "PolePoint")),
                    ("Transformer GLN", ReadText(reader, "TransfGLN"))
                };

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Range Extender",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
        }

        private async Task ReadAntennaMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    a.SiteId,
                    a.SN
                FROM [sgc_equip].[Antenna] a
                WHERE
                    LTRIM(RTRIM(ISNULL(a.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), a.SN))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var serial = ReadText(reader, "SN");

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Antenna",
                    siteId,
                    DetermineMatchField(
                        query,
                        ("Site", siteId),
                        ("Antenna SN", serial)),
                    ("Site", siteId),
                    ("Antenna SN", serial));
            }
        }

        private async Task ReadEnclosureMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TOP (50)
                    e.SiteId,
                    e.SN,
                    e.Model
                FROM [sgc_equip].[Enclosure] e
                WHERE
                    LTRIM(RTRIM(ISNULL(e.SiteId, ''))) = @Query OR
                    LTRIM(RTRIM(CONVERT(nvarchar(150), e.SN))) = @Query;
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");
                var serial = ReadText(reader, "SN");

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Enclosure",
                    siteId,
                    DetermineMatchField(
                        query,
                        ("Site", siteId),
                        ("Enclosure SN", serial)),
                    ("Site", siteId),
                    ("Enclosure SN", serial),
                    ("Model", ReadText(reader, "Model")));
            }
        }

        private async Task ReadSiteMetadataAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            IEnumerable<string> siteIds,
            CancellationToken cancellationToken)
        {
            var sites =
                siteIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(50)
                    .ToList();

            if (sites.Count == 0)
                return;

            var parameterNames =
                sites
                    .Select((_, index) => $"@Site{index}")
                    .ToList();

            var sql =
                $"""
                SELECT
                    s.SiteId,
                    status.CodeValue AS SiteStatus,
                    siteType.CodeValue AS SiteType,
                    cfg.Config AS SiteConfig,
                    cfg.Comm1Type,
                    cfg.Comm2Type,
                    cfg.Descr AS ConfigDescription,
                    addr.StreetNo,
                    addr.StreetName,
                    addr.City,
                    addr.County,
                    addr.StateCode,
                    addr.ZipCode,
                    gps.Latitude,
                    gps.Longitude
                FROM [sgc_main].[Site] s
                LEFT JOIN [sgc_main].[Decode] status
                    ON s.SiteStatus_id = status.CodeId
                   AND status.CodeType = 'SiteStatus'
                LEFT JOIN [sgc_main].[Decode] siteType
                    ON s.SiteType_id = siteType.CodeId
                   AND siteType.CodeType = 'SiteType'
                LEFT JOIN [sgc_main].[Config] cfg
                    ON s.Config_id = cfg.id
                LEFT JOIN [sgc_main].[Address] addr
                    ON s.SiteId = addr.SiteId
                LEFT JOIN [sgc_main].[GPS] gps
                    ON s.SiteId = gps.SiteId
                WHERE s.SiteId IN ({string.Join(", ", parameterNames)});
                """;

            await using var command =
                new SqlCommand(sql, connection)
                {
                    CommandTimeout = ParentCommandTimeoutSeconds
                };

            for (var index = 0; index < sites.Count; index++)
            {
                command.Parameters.Add(
                    new SqlParameter(
                        parameterNames[index],
                        SqlDbType.NVarChar,
                        100)
                    {
                        Value = sites[index]
                    });
            }

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var siteId = ReadText(reader, "SiteId");

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Site",
                    siteId,
                    string.Equals(
                        siteId,
                        response.Query,
                        StringComparison.OrdinalIgnoreCase)
                        ? "Site"
                        : "Related Site",
                    ("Site", siteId),
                    ("Status", ReadText(reader, "SiteStatus")),
                    ("Type", ReadText(reader, "SiteType")),
                    ("Config", ReadText(reader, "SiteConfig")),
                    ("Primary Comm Type", ReadText(reader, "Comm1Type")),
                    ("Secondary Comm Type", ReadText(reader, "Comm2Type")),
                    ("Config Description", ReadText(reader, "ConfigDescription")),
                    ("Street Number", ReadText(reader, "StreetNo")),
                    ("Street Name", ReadText(reader, "StreetName")),
                    ("City", ReadText(reader, "City")),
                    ("County", ReadText(reader, "County")),
                    ("State", ReadText(reader, "StateCode")),
                    ("ZIP", ReadText(reader, "ZipCode")),
                    ("Latitude", ReadText(reader, "Latitude")),
                    ("Longitude", ReadText(reader, "Longitude")));
            }
        }

        private async Task LoadSmartGridSuiteRecordsAsync(
            DeviceLookupResponseDto response,
            string query,
            CancellationToken cancellationToken)
        {
            var directTickets =
                await _db.Tickets
                    .AsNoTracking()
                    .Where(ticket =>
                        ticket.Site == query ||
                        ticket.Notification == query ||
                        ticket.CurrentWorkOrder == query ||
                        ticket.Summary.Contains(query) ||
                        ticket.Problem.Contains(query) ||
                        (ticket.Notes != null &&
                         ticket.Notes.Contains(query)) ||
                        (ticket.DispatchNotes != null &&
                         ticket.DispatchNotes.Contains(query)))
                    .OrderByDescending(ticket => ticket.LastActivityAt)
                    .Take(MaxSmartGridRows)
                    .ToListAsync(cancellationToken);

            var directHistory =
                await _db.SiteHistory
                    .AsNoTracking()
                    .Where(history =>
                        !history.IsDeleted &&
                        (history.SiteId == query ||
                         (history.Narrative != null &&
                          history.Narrative.Contains(query)) ||
                         (history.IssueText != null &&
                          history.IssueText.Contains(query))))
                    .OrderByDescending(history => history.VisitDate)
                    .ThenByDescending(history => history.HistoryId)
                    .Take(MaxSmartGridRows)
                    .ToListAsync(cancellationToken);

            var relatedSiteIds =
                new HashSet<string>(
                    response.ParentRecords
                        .Select(x => x.SiteId)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim()),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var ticket in directTickets)
            {
                if (!string.IsNullOrWhiteSpace(ticket.Site))
                    relatedSiteIds.Add(ticket.Site.Trim());
            }

            foreach (var history in directHistory)
            {
                if (!string.IsNullOrWhiteSpace(history.SiteId))
                    relatedSiteIds.Add(history.SiteId.Trim());
            }

            if (response.ParentRecords.Any(x =>
                    string.Equals(
                        x.RecordType,
                        "Site",
                        StringComparison.OrdinalIgnoreCase)))
            {
                relatedSiteIds.Add(query);
            }

            var ticketById =
                directTickets.ToDictionary(x => x.Id);

            var historyById =
                directHistory.ToDictionary(x => x.HistoryId);

            if (relatedSiteIds.Count > 0)
            {
                var sites =
                    relatedSiteIds
                        .Take(100)
                        .ToList();

                var siteTickets =
                    await _db.Tickets
                        .AsNoTracking()
                        .Where(ticket =>
                            sites.Contains(ticket.Site))
                        .OrderByDescending(ticket => ticket.LastActivityAt)
                        .Take(MaxSmartGridRows)
                        .ToListAsync(cancellationToken);

                foreach (var ticket in siteTickets)
                    ticketById[ticket.Id] = ticket;

                var siteHistory =
                    await _db.SiteHistory
                        .AsNoTracking()
                        .Where(history =>
                            !history.IsDeleted &&
                            sites.Contains(history.SiteId))
                        .OrderByDescending(history => history.VisitDate)
                        .ThenByDescending(history => history.HistoryId)
                        .Take(MaxSmartGridRows)
                        .ToListAsync(cancellationToken);

                foreach (var history in siteHistory)
                    historyById[history.HistoryId] = history;
            }

            response.RelatedSiteIds.AddRange(
                relatedSiteIds);

            response.Tickets =
                ticketById.Values
                    .OrderByDescending(x => x.LastActivityAt)
                    .Take(MaxSmartGridRows)
                    .Select(ticket =>
                        new DeviceLookupTicketDto
                        {
                            TicketId = ticket.Id,
                            Site = ticket.Site ?? string.Empty,
                            Notification = ticket.Notification ?? string.Empty,
                            WorkOrder = ticket.CurrentWorkOrder ?? string.Empty,
                            WorkOrderClass = ticket.WorkOrderClass ?? string.Empty,
                            Status = ticket.Status ?? string.Empty,
                            Summary = ticket.Summary ?? string.Empty,
                            Problem = ticket.Problem ?? string.Empty,
                            DispatchNotes = ticket.DispatchNotes ?? string.Empty,
                            Notes = ticket.Notes ?? string.Empty,
                            CreatedAt = ticket.CreatedAt,
                            LastActivityAt = ticket.LastActivityAt
                        })
                    .ToList();

            response.SiteHistory =
                historyById.Values
                    .OrderByDescending(x => x.VisitDate)
                    .ThenByDescending(x => x.HistoryId)
                    .Take(MaxSmartGridRows)
                    .Select(history =>
                        new DeviceLookupHistoryDto
                        {
                            HistoryId = history.HistoryId,
                            SiteId = history.SiteId ?? string.Empty,
                            VisitDate = history.VisitDate,
                            PrimaryTech = history.PrimaryTech ?? string.Empty,
                            SecondaryTech = history.SecondaryTech ?? string.Empty,
                            IssueText = history.IssueText ?? string.Empty,
                            Narrative = history.Narrative ?? string.Empty,
                            SourceType = history.SourceType ?? string.Empty
                        })
                    .ToList();
        }

        private static SqlCommand CreateParentCommand(
            SqlConnection connection,
            string sql,
            string query)
        {
            var command =
                new SqlCommand(sql, connection)
                {
                    CommandTimeout = ParentCommandTimeoutSeconds
                };

            command.Parameters.Add(
                new SqlParameter(
                    "@Query",
                    SqlDbType.NVarChar,
                    250)
                {
                    Value = query
                });

            return command;
        }

        private static string ReadText(
            SqlDataReader reader,
            string columnName)
        {
            var ordinal =
                reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return string.Empty;

            return Convert.ToString(
                       reader.GetValue(ordinal))
                   ?.Trim()
                   ?? string.Empty;
        }

        private static string DetermineMatchField(
            string query,
            params (string Label, string Value)[] fields)
        {
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Value))
                    continue;

                if (string.Equals(
                        field.Value.Trim(),
                        query.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return field.Label;
                }
            }

            return "Related";
        }

        private static void AddParentRecord(
            DeviceLookupResponseDto response,
            string source,
            string recordType,
            string siteId,
            string matchField,
            params (string Label, string Value)[] fields)
        {
            var cleanFields =
                fields
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Value) &&
                        !string.Equals(
                            x.Value.Trim(),
                            "NA",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(x =>
                        new DeviceLookupFieldDto
                        {
                            Label = x.Label,
                            Value = x.Value.Trim()
                        })
                    .ToList();

            if (cleanFields.Count == 0)
                return;

            var record =
                new DeviceLookupRecordDto
                {
                    Source = source,
                    RecordType = recordType,
                    SiteId = (siteId ?? string.Empty).Trim(),
                    MatchField = matchField,
                    Fields = cleanFields
                };

            var duplicate =
                response.ParentRecords.Any(existing =>
                    string.Equals(
                        existing.RecordType,
                        record.RecordType,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        existing.SiteId,
                        record.SiteId,
                        StringComparison.OrdinalIgnoreCase) &&
                    existing.Fields.Count == record.Fields.Count &&
                    existing.Fields.All(field =>
                        record.Fields.Any(other =>
                            string.Equals(
                                other.Label,
                                field.Label,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                other.Value,
                                field.Value,
                                StringComparison.OrdinalIgnoreCase))));

            if (!duplicate)
                response.ParentRecords.Add(record);

            if (!string.IsNullOrWhiteSpace(record.SiteId))
                response.RelatedSiteIds.Add(record.SiteId);
        }
    }
}
