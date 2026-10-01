using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Contracts.Dispatcher;
using System.Data;
using System.Data.Common;

namespace SmartGridSuite.Api.Services
{
    public sealed class DeviceLookupService
    {
        private const int ParentCommandTimeoutSeconds = 3;
        private const int ParentSearchBudgetSeconds = 6;
        private const int SearchBudgetSeconds = 12;
        private const int MaxSmartGridRows = 250;

        // Scoped service: metadata belongs to this search/connection, not a
        // process-wide cache that can mix different Parent DB schemas.
        private readonly Dictionary<string, (string SqlType, int? MaxLength)> _parentColumnTypes = new();

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
            DeviceLookupSearchType searchType = DeviceLookupSearchType.DeviceSerialNumber,
            CancellationToken cancellationToken = default)
        {
            query = (query ?? string.Empty).Trim();
            var response = new DeviceLookupResponseDto { Query = query };
            if (string.IsNullOrWhiteSpace(query))
                return response;
            if (!Enum.IsDefined(searchType))
                throw new ArgumentOutOfRangeException(nameof(searchType));

            // The client has a 15-second timeout. Reserve time for related history
            // and return records already read if either database is slow.
            using var searchBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            searchBudget.CancelAfter(TimeSpan.FromSeconds(SearchBudgetSeconds));

            if (searchType is not (DeviceLookupSearchType.Notification or
                DeviceLookupSearchType.WorkOrder or DeviceLookupSearchType.HistoryText))
            {
                using var parentBudget = CancellationTokenSource.CreateLinkedTokenSource(searchBudget.Token);
                parentBudget.CancelAfter(TimeSpan.FromSeconds(ParentSearchBudgetSeconds));
                try
                {
                    await LoadParentDatabaseRecordsAsync(response, query, searchType, parentBudget.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    AddWarning(response, "Parent DB search reached its time limit; device results may be incomplete.");
                }
                catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
                {
                    AddWarning(response, "Parent DB search could not finish; showing available results.");
                }
            }

            try
            {
                await LoadSmartGridSuiteRecordsAsync(response, query, searchType, searchBudget.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AddWarning(response, "Related-record search reached its time limit; results may be incomplete.");
            }
            catch (DbException)
            {
                AddWarning(response, "SmartGridSuite records could not be loaded; showing available device results.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            response.RelatedSiteIds = response.RelatedSiteIds
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            response.Tickets = response.Tickets.DistinctBy(x => x.TicketId)
                .OrderByDescending(x => x.LastActivityAt).Take(MaxSmartGridRows).ToList();
            response.SiteHistory = response.SiteHistory.DistinctBy(x => x.HistoryId)
                .OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.HistoryId)
                .Take(MaxSmartGridRows).ToList();
            response.SiteNotes = response.SiteNotes.DistinctBy(x => x.Id)
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(MaxSmartGridRows).ToList();
            return response;
        }

        private async Task LoadParentDatabaseRecordsAsync(
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            await using var connection = _parentDatabaseConnectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await ReadParentColumnTypesAsync(connection, cancellationToken);

            if (searchType == DeviceLookupSearchType.Sim)
            {
                await ReadPmrMatchesAsync(connection, response, query, searchType, cancellationToken);
            }
            else
            {
                if (searchType == DeviceLookupSearchType.DeviceSerialNumber)
                    await ReadPmrMatchesAsync(connection, response, query, searchType, cancellationToken);
                await ReadLteMatchesAsync(connection, response, query, searchType, cancellationToken);
                await ReadAmsMatchesAsync(connection, response, query, searchType, cancellationToken);
                await ReadIgsdMatchesAsync(connection, response, query, searchType, cancellationToken);
                if (searchType is DeviceLookupSearchType.Site or DeviceLookupSearchType.IpAddress)
                    await ReadRadio700MatchesAsync(connection, response, query, searchType, cancellationToken);
                if (searchType is DeviceLookupSearchType.Site or DeviceLookupSearchType.DeviceSerialNumber)
                {
                    await ReadRangeExtenderMatchesAsync(connection, response, query, searchType, cancellationToken);
                    await ReadAntennaMatchesAsync(connection, response, query, searchType, cancellationToken);
                    await ReadEnclosureMatchesAsync(connection, response, query, searchType, cancellationToken);
                }
                if (searchType == DeviceLookupSearchType.Site)
                    await ReadPmrMatchesAsync(connection, response, query, searchType, cancellationToken);
                else if (searchType == DeviceLookupSearchType.IpAddress)
                {
                    // Only look up PMRs for AMS rows actually matched by the IP.
                    var serials = response.ParentRecords.Where(x => x.RecordType == "AMS / MR")
                        .SelectMany(x => x.Fields).Where(x => x.Label == "iTron_CR_Num")
                        .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
                    foreach (var serial in serials)
                        await ReadPmrMatchesAsync(connection, response, serial,
                            DeviceLookupSearchType.DeviceSerialNumber, cancellationToken);
                }
            }

            var parentSiteIds = response.RelatedSiteIds.ToList();
            if (searchType == DeviceLookupSearchType.Site)
                parentSiteIds.Add(query);
            await ReadSiteMetadataAsync(connection, response, parentSiteIds, cancellationToken);
        }

        private async Task ReadPmrMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType switch
            {
                DeviceLookupSearchType.Sim => ParentPredicate("sgc_equip.PMR", "p", "ATTSlot1", "VzwSlot2"),
                DeviceLookupSearchType.Site => PmrSitePredicate(),
                _ => ParentPredicate("sgc_equip.PMR", "p", "SN")
            };

            var sql = $"""
                SELECT TOP (50)
                    p.*,
                    a.SiteId AS CurrentSiteId,
                    a.RadioSN AS AssociatedAmsRadioSN,
                    a.RadioIP AS AssociatedAmsRadioIP,
                    a.EthernetIP AS AssociatedAmsEthernetIP,
                    l.IP1 AS AssociatedLteWanIp
                FROM (SELECT TOP (50) p.* FROM [sgc_equip].[PMR] p WHERE {predicate}) p
                LEFT JOIN [sgc_comm].[AMS] a
                    ON a.iTron_CR_Num = {ParentValueExpression("sgc_comm.AMS", "iTron_CR_Num", "p.SN")}
                LEFT JOIN [sgc_equip].[LTE] l
                    ON a.SiteId = l.SiteId
                ;
                """;

            await using var command =
                CreateParentCommand(
                    connection,
                    sql,
                    query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var fields =
                    ReadAllFields(reader);

                /*
                 * A PMR may no longer have a current AMS association. If the
                 * PMR table itself carries a SiteId, preserve it as historical/
                 * equipment context before falling back to the current AMS site.
                 */
                var siteId =
                    GetFieldValue(
                        fields,
                        "SiteId");

                if (string.IsNullOrWhiteSpace(siteId))
                {
                    siteId =
                        GetFieldValue(
                            fields,
                            "CurrentSiteId");
                }

                var currentSiteId = GetFieldValue(fields, "CurrentSiteId");
                if (!string.IsNullOrWhiteSpace(currentSiteId))
                    response.RelatedSiteIds.Add(currentSiteId);

                AddParentRecord(
                    response,
                    source: "Parent DB",
                    recordType: "PMR",
                    siteId,
                    DetermineMatchField(
                        query,
                        fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadLteMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType switch
            {
                DeviceLookupSearchType.Site => ParentPredicate("sgc_equip.LTE", "l", "SiteId"),
                DeviceLookupSearchType.IpAddress => ParentPredicate("sgc_equip.LTE", "l", "IP1"),
                _ => ParentPredicate("sgc_equip.LTE", "l", "SN")
            };

            var sql = $"""
                SELECT TOP (50)
                    l.*
                FROM [sgc_equip].[LTE] l
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "LTE",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadAmsMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType switch
            {
                DeviceLookupSearchType.Site => ParentPredicate("sgc_comm.AMS", "a", "SiteId"),
                DeviceLookupSearchType.IpAddress => ParentPredicate("sgc_comm.AMS", "a", "RadioIP", "EthernetIP"),
                _ => ParentPredicate("sgc_comm.AMS", "a", "RadioSN", "iTron_CR_Num")
            };

            var sql = $"""
                SELECT TOP (50)
                    a.*
                FROM [sgc_comm].[AMS] a
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "AMS / MR",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadIgsdMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType switch
            {
                DeviceLookupSearchType.Site => ParentPredicate("sgc_comm.IGSD", "i", "SiteId"),
                DeviceLookupSearchType.IpAddress => ParentPredicate("sgc_comm.IGSD", "i", "RadioIP", "PriProtLanDigi", "PriWanOut", "PriDigiWanOut", "PriProtLanSubN", "PriProtLanRtu", "SecDigiWanOut", "SecProtLanDigi", "SecProtLanSubN", "SecProtLanRtu"),
                _ => ParentPredicate("sgc_comm.IGSD", "i", "RadioSN", "Cyberlock")
            };

            var sql = $"""
                SELECT TOP (50)
                    i.*
                FROM [sgc_comm].[IGSD] i
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "IGSD",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadRadio700MatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType == DeviceLookupSearchType.Site
                ? ParentPredicate("sgc_equip.Radio700", "r", "SiteId")
                : ParentPredicate("sgc_equip.Radio700", "r", "RadioIP", "RtuWanVLAN", "RtuWanVLANGateway");

            var sql = $"""
                SELECT TOP (50)
                    r.*
                FROM [sgc_equip].[Radio700] r
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Radio700 / DACS",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadRangeExtenderMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType == DeviceLookupSearchType.Site
                ? ParentPredicate("sgc_comm.RE", "r", "SiteId")
                : ParentPredicate("sgc_comm.RE", "r", "MeterNumber", "MACAddress");

            var sql = $"""
                SELECT TOP (50)
                    r.*
                FROM [sgc_comm].[RE] r
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Range Extender",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadAntennaMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType == DeviceLookupSearchType.Site ? ParentPredicate("sgc_equip.Antenna", "a", "SiteId") : ParentPredicate("sgc_equip.Antenna", "a", "SN");

            var sql = $"""
                SELECT TOP (50)
                    a.*
                FROM [sgc_equip].[Antenna] a
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Antenna",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
        }

        private async Task ReadEnclosureMatchesAsync(
            SqlConnection connection,
            DeviceLookupResponseDto response,
            string query,
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            var predicate = searchType == DeviceLookupSearchType.Site ? ParentPredicate("sgc_equip.Enclosure", "e", "SiteId") : ParentPredicate("sgc_equip.Enclosure", "e", "SN");

            var sql = $"""
                SELECT TOP (50)
                    e.*
                FROM [sgc_equip].[Enclosure] e
                WHERE {predicate};
                """;

            await using var command =
                CreateParentCommand(connection, sql, query);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var rowsRead = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowsRead++;
                var siteId = ReadText(reader, "SiteId");
                var fields = ReadAllFields(reader);

                AddParentRecord(
                    response,
                    "Parent DB",
                    "Enclosure",
                    siteId,
                    DetermineMatchField(query, fields),
                    fields);
            }
            if (rowsRead == 50)
                AddWarning(response, "Device records are limited to 50 matches per category; narrow the identifier if needed.");
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
                        250)
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
            DeviceLookupSearchType searchType,
            CancellationToken cancellationToken)
        {
            if (searchType == DeviceLookupSearchType.Site)
                response.RelatedSiteIds.Add(query);

            // Free-text scans are opt-in. Identifier searches load history/notes
            // through related SiteIds instead of scanning every narrative.
            if (searchType is DeviceLookupSearchType.Notification or
                DeviceLookupSearchType.WorkOrder or DeviceLookupSearchType.HistoryText)
            {
                var tickets = _db.Tickets.AsNoTracking();
                tickets = searchType switch
                {
                    DeviceLookupSearchType.Notification => tickets.Where(x => x.Notification == query),
                    DeviceLookupSearchType.WorkOrder => tickets.Where(x => x.CurrentWorkOrder == query),
                    _ => tickets.Where(x => x.Summary.Contains(query) || x.Problem.Contains(query) ||
                        (x.Notes != null && x.Notes.Contains(query)) ||
                        (x.DispatchNotes != null && x.DispatchNotes.Contains(query)))
                };
                AddTickets(response, await tickets.OrderByDescending(x => x.LastActivityAt)
                    .Take(MaxSmartGridRows).ToListAsync(cancellationToken));
            }

            if (searchType == DeviceLookupSearchType.HistoryText)
            {
                AddHistory(response, await _db.SiteHistory.AsNoTracking()
                    .Where(x => !x.IsDeleted && ((x.Narrative != null && x.Narrative.Contains(query)) ||
                        (x.IssueText != null && x.IssueText.Contains(query))))
                    .OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.HistoryId)
                    .Take(MaxSmartGridRows).ToListAsync(cancellationToken));
                AddSiteNotes(response, await _db.SiteNotes.AsNoTracking()
                    .Where(x => x.NoteText.Contains(query))
                    .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                    .Take(MaxSmartGridRows).ToListAsync(cancellationToken));
            }

            var sites = response.RelatedSiteIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
            if (sites.Count == 0)
                return;

            // Commit each category to the response immediately so later timeouts
            // do not discard the results already retrieved.
            AddTickets(response, await _db.Tickets.AsNoTracking()
                .Where(x => sites.Contains(x.Site)).OrderByDescending(x => x.LastActivityAt)
                .Take(MaxSmartGridRows).ToListAsync(cancellationToken));
            AddHistory(response, await _db.SiteHistory.AsNoTracking()
                .Where(x => !x.IsDeleted && sites.Contains(x.SiteId))
                .OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.HistoryId)
                .Take(MaxSmartGridRows).ToListAsync(cancellationToken));
            AddSiteNotes(response, await _db.SiteNotes.AsNoTracking()
                .Where(x => sites.Contains(x.SiteId)).OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .Take(MaxSmartGridRows).ToListAsync(cancellationToken));
        }

        private static void AddTickets(DeviceLookupResponseDto response, List<TicketEntity> tickets)
        {
            WarnIfLimited(response, tickets.Count);
            response.RelatedSiteIds.AddRange(tickets.Select(x => x.Site));
            response.Tickets.AddRange(tickets.Select(ticket => new DeviceLookupTicketDto
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
            }));
        }

        private static void AddHistory(DeviceLookupResponseDto response, List<SiteHistoryEntity> history)
        {
            WarnIfLimited(response, history.Count);
            response.RelatedSiteIds.AddRange(history.Select(x => x.SiteId));
            response.SiteHistory.AddRange(history.Select(x => new DeviceLookupHistoryDto
            {
                HistoryId = x.HistoryId,
                SiteId = x.SiteId ?? string.Empty,
                VisitDate = x.VisitDate,
                PrimaryTech = x.PrimaryTech ?? string.Empty,
                SecondaryTech = x.SecondaryTech ?? string.Empty,
                IssueText = x.IssueText ?? string.Empty,
                Narrative = x.Narrative ?? string.Empty,
                SourceType = x.SourceType ?? string.Empty
            }));
        }

        private static void AddSiteNotes(DeviceLookupResponseDto response, List<SiteNoteEntity> notes)
        {
            WarnIfLimited(response, notes.Count);
            response.RelatedSiteIds.AddRange(notes.Select(x => x.SiteId));
            response.SiteNotes.AddRange(notes.Select(x => new DeviceLookupSiteNoteDto
            {
                Id = x.Id,
                SiteId = x.SiteId ?? string.Empty,
                NoteType = x.NoteType ?? string.Empty,
                NoteText = x.NoteText ?? string.Empty,
                IsActive = x.IsActive,
                CreatedBy = x.CreatedBy ?? string.Empty,
                CreatedAt = x.CreatedAt,
                UpdatedBy = x.UpdatedBy ?? string.Empty,
                UpdatedAt = x.UpdatedAt
            }));
        }

        private static void WarnIfLimited(DeviceLookupResponseDto response, int count)
        {
            if (count == MaxSmartGridRows)
                AddWarning(response, "Tickets, history, and notes are limited to the most recent 250 matches per category.");
        }

        private static void AddWarning(DeviceLookupResponseDto response, string warning)
        {
            if (!response.Warning.Contains(warning, StringComparison.Ordinal))
                response.Warning = string.IsNullOrWhiteSpace(response.Warning)
                    ? warning : response.Warning + " " + warning;
        }

        private async Task ReadParentColumnTypesAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE,
                       CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE (TABLE_SCHEMA = 'sgc_equip' AND TABLE_NAME IN ('PMR', 'LTE', 'Radio700', 'Antenna', 'Enclosure'))
                   OR (TABLE_SCHEMA = 'sgc_comm' AND TABLE_NAME IN ('AMS', 'IGSD', 'RE'));
                """;
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = ParentCommandTimeoutSeconds };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            _parentColumnTypes.Clear();
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = ReadText(reader, "DATA_TYPE").ToLowerInvariant();
                int? maxLength = null;
                if (type is "char" or "varchar" or "nchar" or "nvarchar")
                {
                    var length = Convert.ToInt32(reader["CHARACTER_MAXIMUM_LENGTH"]);
                    if (length > 0)
                        maxLength = length;
                    type += length == -1 ? "(max)" : $"({length})";
                }
                else if (type is "decimal" or "numeric")
                    type += $"({Convert.ToInt32(reader["NUMERIC_PRECISION"])},{Convert.ToInt32(reader["NUMERIC_SCALE"])})";
                else if (type is not ("tinyint" or "smallint" or "int" or "bigint" or "bit" or "float" or
                    "real" or "money" or "smallmoney" or "uniqueidentifier"))
                    continue;

                var key = $"{ReadText(reader, "TABLE_SCHEMA")}.{ReadText(reader, "TABLE_NAME")}.{ReadText(reader, "COLUMN_NAME")}";
                _parentColumnTypes[key.ToUpperInvariant()] = (type, maxLength);
            }
        }

        private string ParentValueExpression(string table, string column, string value)
        {
            if (!_parentColumnTypes.TryGetValue($"{table}.{column}".ToUpperInvariant(), out var metadata))
                throw new InvalidOperationException($"Parent DB identifier column is unavailable: {table}.{column}");
            return $"TRY_CONVERT({metadata.SqlType}, {value})";
        }

        private string PmrSitePredicate()
        {
            var associatedSite = "p.SN IN (SELECT " +
                ParentValueExpression("sgc_equip.PMR", "SN", "a.iTron_CR_Num") +
                " FROM [sgc_comm].[AMS] a WHERE " + ParentPredicate("sgc_comm.AMS", "a", "SiteId") + ")";
            return _parentColumnTypes.ContainsKey("SGC_EQUIP.PMR.SITEID")
                ? ParentPredicate("sgc_equip.PMR", "p", "SiteId") + " OR " + associatedSite
                : associatedSite;
        }

        private string ParentPredicate(string table, string alias, params string[] columns)
        {
            // Convert the parameter to the column's native type, never the
            // indexed column to text. TRY_CONVERT also lets an alphanumeric SN
            // safely pass tables whose identifiers are numeric.
            return string.Join(" OR ", columns.Select(column =>
            {
                var value = ParentValueExpression(table, column, "@Query");
                var metadata = _parentColumnTypes[$"{table}.{column}".ToUpperInvariant()];
                var lengthGuard = metadata.MaxLength is int length ? $"LEN(@Query) <= {length} AND " : string.Empty;
                return $"({lengthGuard}{alias}.{column} = {value})";
            }));
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

        private static (string Label, string Value)[] ReadAllFields(
            SqlDataReader reader)
        {
            var fields =
                new List<(string Label, string Value)>();

            for (var index = 0;
                 index < reader.FieldCount;
                 index++)
            {
                if (reader.IsDBNull(index))
                    continue;

                var name =
                    reader.GetName(index);

                var raw =
                    reader.GetValue(index);

                string value;

                if (raw is byte[] bytes)
                {
                    value =
                        Convert.ToHexString(bytes);
                }
                else
                {
                    value =
                        Convert.ToString(raw)
                        ?.Trim()
                        ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                fields.Add(
                    (name, value));
            }

            return fields.ToArray();
        }

        private static string GetFieldValue(
            IEnumerable<(string Label, string Value)> fields,
            string label)
        {
            return fields
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Label,
                        label,
                        StringComparison.OrdinalIgnoreCase))
                .Value
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
