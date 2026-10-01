using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Contracts.Dispatcher;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace SmartGridSuite.Api.Services
{
    public sealed class DeviceLookupService
    {
        private const int ParentCommandTimeoutSeconds = 15;
        private const int ParentSearchBudgetSeconds = 40;
        private const int SearchBudgetSeconds = 50;
        private const int MaxSmartGridRows = 250;

        // Scoped service: metadata belongs to this search/connection, not a
        // process-wide cache that can mix different Parent DB schemas.
        private readonly Dictionary<string, (string SqlType, int? MaxLength)> _parentColumnTypes = new();

        private readonly HashSet<string> _loadedParentTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _parentTextFallbackColumns = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SqlParameter> _parentQueryParameters = new();
        private readonly ILogger<DeviceLookupService> _logger;
        private string _parentStage = "connection";

        private readonly SmartGridDbContext _db;
        private readonly ParentDatabaseConnectionFactory _parentDatabaseConnectionFactory;

        public DeviceLookupService(
            SmartGridDbContext db,
            ParentDatabaseConnectionFactory parentDatabaseConnectionFactory,
            ILogger<DeviceLookupService>? logger = null)
        {
            _logger = logger ?? NullLogger<DeviceLookupService>.Instance;
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

            // Device Lookup has a dedicated 60-second client timeout. Reserve time for related history
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
                    _logger.LogWarning("Device Lookup Parent DB budget expired during {Stage}", _parentStage);
                    AddWarning(response, $"Parent DB search reached its 40-second time limit during {_parentStage}; device results may be incomplete.");
                }
                catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
                {
                    ReportParentFailure(response, _parentStage, ex);
                }
            }

            CorrelatePmrRecords(response);

            try
            {
                await LoadSmartGridSuiteRecordsAsync(response, query, searchType, searchBudget.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AddWarning(response, "Related-record search reached its time limit; results may be incomplete.");
            }
            catch (DbException ex)
            {
                _logger.LogWarning(ex, "Device Lookup could not load SmartGridSuite records");
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
            _parentStage = "connection";
            await using var connection = _parentDatabaseConnectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            Task ReadTable(string table, Func<Task> read) => RunParentCategoryAsync(response, table, async () =>
            {
                _parentQueryParameters.Clear();
                _parentStage = table + " schema";
                await ReadParentColumnTypesAsync(connection, table, cancellationToken);
                _parentStage = table + " query";
                await read();
            }, cancellationToken);

            if (searchType == DeviceLookupSearchType.Sim)
            {
                await ReadTable("sgc_equip.PMR", () => ReadPmrMatchesAsync(connection, response, query, searchType, cancellationToken));
                // A detached PMR remains visible even when association lookup fails.
                var pmrSerials = response.ParentRecords.Where(x => x.RecordType == "PMR")
                    .SelectMany(x => x.Fields).Where(x => x.Label.Equals("SN", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
                foreach (var serial in pmrSerials)
                    await ReadTable("sgc_comm.AMS", () => ReadAmsMatchesAsync(connection, response, serial,
                        DeviceLookupSearchType.DeviceSerialNumber, cancellationToken));
            }
            else
            {
                // Search communications records before auxiliary equipment so a
                // slow/unavailable PMR table cannot prevent radio serial matches.
                await ReadTable("sgc_comm.AMS", () => ReadAmsMatchesAsync(connection, response, query, searchType, cancellationToken));
                await ReadTable("sgc_comm.IGSD", () => ReadIgsdMatchesAsync(connection, response, query, searchType, cancellationToken));
                await ReadTable("sgc_equip.LTE", () => ReadLteMatchesAsync(connection, response, query, searchType, cancellationToken));
                await ReadTable("sgc_equip.Radio700", () => ReadRadio700MatchesAsync(connection, response, query, searchType, cancellationToken));

                if (searchType != DeviceLookupSearchType.IpAddress)
                    await ReadTable("sgc_equip.PMR", () => ReadPmrMatchesAsync(connection, response, query, searchType, cancellationToken));

                if (searchType is DeviceLookupSearchType.IpAddress or DeviceLookupSearchType.DeviceSerialNumber)
                {
                    var serials = response.ParentRecords.Where(x => x.RecordType == "AMS / MR")
                        .SelectMany(x => x.Fields).Where(x => x.Label.Equals("iTron_CR_Num", StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
                    foreach (var serial in serials)
                    {
                        if (response.ParentRecords.Any(x => x.RecordType == "PMR" &&
                            x.Fields.Any(f => f.Label.Equals("SN", StringComparison.OrdinalIgnoreCase) &&
                                f.Value.Equals(serial, StringComparison.OrdinalIgnoreCase))))
                            continue;
                        await ReadTable("sgc_equip.PMR", () => ReadPmrMatchesAsync(connection, response, serial,
                            DeviceLookupSearchType.DeviceSerialNumber, cancellationToken));
                    }
                }

                if (searchType is DeviceLookupSearchType.Site or DeviceLookupSearchType.DeviceSerialNumber)
                {
                    await ReadTable("sgc_comm.RE", () => ReadRangeExtenderMatchesAsync(connection, response, query, searchType, cancellationToken));
                    await ReadTable("sgc_equip.Antenna", () => ReadAntennaMatchesAsync(connection, response, query, searchType, cancellationToken));
                    await ReadTable("sgc_equip.Enclosure", () => ReadEnclosureMatchesAsync(connection, response, query, searchType, cancellationToken));
                }
            }

            CorrelatePmrRecords(response);
            var parentSiteIds = response.RelatedSiteIds.ToList();
            if (searchType == DeviceLookupSearchType.Site)
                parentSiteIds.Add(query);
            _parentStage = "site metadata";
            await RunParentCategoryAsync(response, _parentStage,
                () => ReadSiteMetadataAsync(connection, response, parentSiteIds, cancellationToken), cancellationToken);
        }

        private async Task RunParentCategoryAsync(DeviceLookupResponseDto response, string category,
            Func<Task> read, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await read();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                ex is SqlException or InvalidOperationException or TimeoutException)
            {
                ReportParentFailure(response, category, ex);
            }
        }

        private void ReportParentFailure(DeviceLookupResponseDto response, string stage, Exception ex)
        {
            _logger.LogWarning(ex, "Device Lookup Parent DB failure during {Stage}", stage);
            var reason = ex is SqlException sql && sql.Number == -2
                ? $"SQL command exceeded {ParentCommandTimeoutSeconds} seconds"
                : ex is SqlException sqlError ? $"SQL {sqlError.Number}: {ConciseError(ex.Message)}"
                : ConciseError(ex.Message);
            AddWarning(response, $"Parent DB {stage} failed: {reason}. Results may be incomplete.");
        }

        private static string ConciseError(string message)
        {
            var line = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Unknown error";
            return line.Length > 250 ? line[..250] + "…" : line.TrimEnd('.');
        }

        private static void CorrelatePmrRecords(DeviceLookupResponseDto response)
        {
            foreach (var pmr in response.ParentRecords.Where(x => x.RecordType == "PMR"))
            {
                var sn = pmr.Fields.FirstOrDefault(x => x.Label.Equals("SN", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.IsNullOrWhiteSpace(sn))
                    continue;
                var associations = response.ParentRecords.Where(x => x.RecordType == "AMS / MR" &&
                    x.Fields.Any(f => f.Label.Equals("iTron_CR_Num", StringComparison.OrdinalIgnoreCase) &&
                        f.Value.Equals(sn, StringComparison.OrdinalIgnoreCase))).ToList();
                foreach (var ams in associations)
                {
                    if (!string.IsNullOrWhiteSpace(ams.SiteId))
                    {
                        response.RelatedSiteIds.Add(ams.SiteId);
                        if (string.IsNullOrWhiteSpace(pmr.SiteId))
                            pmr.SiteId = ams.SiteId;
                        AddContextField(pmr, "CurrentSiteId", ams.SiteId);
                    }
                    foreach (var (source, label) in new[] { ("RadioSN", "AssociatedAmsRadioSN"),
                        ("RadioIP", "AssociatedAmsRadioIP"), ("EthernetIP", "AssociatedAmsEthernetIP") })
                        AddContextField(pmr, label, ams.Fields.FirstOrDefault(x => x.Label.Equals(source, StringComparison.OrdinalIgnoreCase))?.Value);
                }
            }
        }

        private static void AddContextField(DeviceLookupRecordDto record, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !record.Fields.Any(x => x.Label == label && x.Value == value))
                record.Fields.Add(new DeviceLookupFieldDto { Label = label, Value = value });
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
                DeviceLookupSearchType.Sim => ParentPredicate("sgc_equip.PMR", "p", query, "ATTSlot1", "VzwSlot2"),
                DeviceLookupSearchType.Site => PmrSitePredicate(response, query),
                _ => ParentPredicate("sgc_equip.PMR", "p", query, "SN")
            };

            var sql = $"""
                SELECT TOP (50) p.*
                FROM [sgc_equip].[PMR] p
                WHERE {predicate};
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
                DeviceLookupSearchType.Site => ParentPredicate("sgc_equip.LTE", "l", query, "SiteId"),
                DeviceLookupSearchType.IpAddress => ParentPredicate("sgc_equip.LTE", "l", query, "IP1", "IP2"),
                _ => ParentPredicate("sgc_equip.LTE", "l", query, "SN")
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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
                DeviceLookupSearchType.Site => ParentPredicate("sgc_comm.AMS", "a", query, "SiteId"),
                DeviceLookupSearchType.IpAddress => ParentPredicate("sgc_comm.AMS", "a", query, "RadioIP", "EthernetIP"),
                _ => ParentPredicate("sgc_comm.AMS", "a", query, "RadioSN", "iTron_CR_Num")
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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
                DeviceLookupSearchType.Site => ParentPredicate("sgc_comm.IGSD", "i", query, "SiteId"),
                DeviceLookupSearchType.IpAddress => ParentPredicate("sgc_comm.IGSD", "i", query, "RadioIP", "PriProtLanDigi", "PriWanOut", "PriDigiWanOut", "PriProtLanSubN", "PriProtLanRtu", "SecDigiWanOut", "SecProtLanDigi", "SecProtLanSubN", "SecProtLanRtu"),
                _ => ParentPredicate("sgc_comm.IGSD", "i", query, "RadioSN", "Cyberlock")
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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
            if (searchType == DeviceLookupSearchType.DeviceSerialNumber &&
                !new[] { "SN", "RadioSN", "SerialNumber" }.Any(column =>
                    _parentColumnTypes.ContainsKey($"sgc_equip.Radio700.{column}".ToUpperInvariant())))
                return;
            var predicate = searchType switch
            {
                DeviceLookupSearchType.Site => ParentPredicate("sgc_equip.Radio700", "r", query, "SiteId"),
                DeviceLookupSearchType.DeviceSerialNumber => ParentPredicate("sgc_equip.Radio700", "r", query, "SN", "RadioSN", "SerialNumber"),
                _ => ParentPredicate("sgc_equip.Radio700", "r", query, "RadioIP", "RtuWanVLAN", "RtuWanVLANGateway")
            };

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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
                ? ParentPredicate("sgc_comm.RE", "r", query, "SiteId")
                : ParentPredicate("sgc_comm.RE", "r", query, "MeterNumber", "MACAddress");

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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
            var predicate = searchType == DeviceLookupSearchType.Site ? ParentPredicate("sgc_equip.Antenna", "a", query, "SiteId") : ParentPredicate("sgc_equip.Antenna", "a", query, "SN");

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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
            var predicate = searchType == DeviceLookupSearchType.Site ? ParentPredicate("sgc_equip.Enclosure", "e", query, "SiteId") : ParentPredicate("sgc_equip.Enclosure", "e", query, "SN");

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
                var fields = ReadAllFields(reader);
                var siteId = GetFieldValue(fields, "SiteId");

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
            {
                await PopulateTicketWriteUpsAsync(response, cancellationToken);
                return;
            }

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

            var historyQuery = _db.SiteHistory.AsNoTracking()
                .Where(x => !x.IsDeleted && sites.Contains(x.SiteId));

            // A Site search is explicitly asking for the site's complete history.
            // Identifier searches should only return the history rows that actually
            // mention the identifier instead of every visit ever recorded at a
            // site where that device happened to be installed.
            if (searchType != DeviceLookupSearchType.Site)
            {
                historyQuery = historyQuery.Where(x =>
                    (x.Narrative != null && x.Narrative.Contains(query)) ||
                    (x.IssueText != null && x.IssueText.Contains(query)));
            }

            AddHistory(response, await historyQuery
                .OrderByDescending(x => x.VisitDate)
                .ThenByDescending(x => x.HistoryId)
                .Take(MaxSmartGridRows)
                .ToListAsync(cancellationToken));

            await PopulateTicketWriteUpsAsync(response, cancellationToken);
        }

        private async Task PopulateTicketWriteUpsAsync(
            DeviceLookupResponseDto response,
            CancellationToken cancellationToken)
        {
            var ticketIds = response.Tickets
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return;

            var submissions = await _db.TicketWriteUpSubmissions
                .AsNoTracking()
                .Where(x => ticketIds.Contains(x.TicketId) && !x.IsDeleted)
                .OrderByDescending(x => x.SubmittedAt)
                .ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);

            var latestByTicket = submissions
                .GroupBy(x => x.TicketId)
                .ToDictionary(x => x.Key, x => x.First());

            foreach (var ticket in response.Tickets)
            {
                if (!latestByTicket.TryGetValue(ticket.TicketId, out var submission))
                    continue;

                ticket.SubmittedWriteUp = submission.SubmittedNarrative ?? string.Empty;
                ticket.WriteUpSubmittedBy = string.IsNullOrWhiteSpace(submission.SubmittedByName)
                    ? submission.SubmittedByEmployeeId ?? string.Empty
                    : submission.SubmittedByName.Trim();
                ticket.WriteUpSubmittedAt = submission.SubmittedAt;
            }
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

        private async Task ReadParentColumnTypesAsync(SqlConnection connection, string table, CancellationToken cancellationToken)
        {
            if (_loadedParentTables.Contains(table))
                return;
            var names = table.Split('.');
            var sql = $"SELECT TOP (0) * FROM [{names[0]}].[{names[1]}];";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = ParentCommandTimeoutSeconds };
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            RegisterParentColumns(table, reader.GetColumnSchema());
            _loadedParentTables.Add(table);
        }

        private void RegisterParentColumns(string table, IEnumerable<DbColumn> columns)
        {
            foreach (var column in columns)
            {
                var type = (column.DataTypeName ?? string.Empty).ToLowerInvariant();
                var key = $"{table}.{column.ColumnName}".ToUpperInvariant();
                int? maxLength = null;
                if (type is "char" or "varchar" or "nchar" or "nvarchar")
                {
                    var length = column.ColumnSize ?? 250;
                    var isMax = length <= 0 || length > (type.StartsWith('n') ? 4000 : 8000);
                    if (!isMax)
                        maxLength = length;
                    type += isMax ? "(max)" : $"({length})";
                }
                else if (type is "decimal" or "numeric")
                    type += $"({column.NumericPrecision ?? 38},{column.NumericScale ?? 0})";
                else if (type is "text" or "ntext")
                {
                    type = "nvarchar(250)";
                    _parentTextFallbackColumns.Add(key);
                }
                else if (type is not ("tinyint" or "smallint" or "int" or "bigint" or "bit" or "float" or
                    "real" or "money" or "smallmoney" or "uniqueidentifier"))
                    continue;
                _parentColumnTypes[key] = (type, maxLength);
            }
        }

        private string PmrSitePredicate(DeviceLookupResponseDto response, string query)
        {
            var predicates = new List<string>();
            if (_parentColumnTypes.ContainsKey("SGC_EQUIP.PMR.SITEID"))
                predicates.Add(ParentPredicate("sgc_equip.PMR", "p", query, "SiteId"));
            // AMS rows have already been read for this site. Bind their PMR
            // identifiers directly instead of converting identifiers in a SQL join.
            var serials = response.ParentRecords.Where(x => x.RecordType == "AMS / MR" &&
                    x.SiteId.Equals(query, StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Fields).Where(x => x.Label.Equals("iTron_CR_Num", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(50);
            foreach (var serial in serials)
                predicates.Add(ParentPredicate("sgc_equip.PMR", "p", serial, "SN"));
            return predicates.Count == 0 ? "1 = 0" : string.Join(" OR ", predicates);
        }

        private string ParentPredicate(string table, string alias, string query, params string[] columns)
        {
            var available = columns.Where(column =>
                _parentColumnTypes.ContainsKey($"{table}.{column}".ToUpperInvariant())).ToList();
            if (available.Count == 0)
                throw new InvalidOperationException($"No supported identifier columns in {table}: {string.Join(", ", columns)}");
            return string.Join(" OR ", available.Select(column =>
            {
                var key = $"{table}.{column}".ToUpperInvariant();
                var name = $"@Lookup{_parentQueryParameters.Count}";
                _parentQueryParameters.Add(CreateNativeParameter(name, _parentColumnTypes[key], query));
                if (_parentTextFallbackColumns.Contains(key))
                    return $"(CONVERT(nvarchar(250), {alias}.{column}) = {name})";
                // Bare columns and native parameters allow existing indexes to
                // be used without requiring newer SQL conversion functions.
                return $"({alias}.{column} = {name})";
            }));
        }

        private static SqlParameter CreateNativeParameter(string name,
            (string SqlType, int? MaxLength) metadata, string query)
        {
            var typeName = metadata.SqlType.Split('(')[0];
            var type = typeName == "numeric" ? SqlDbType.Decimal : Enum.Parse<SqlDbType>(typeName, true);
            var parameter = new SqlParameter(name, type);
            object? value = null;
            var invariant = CultureInfo.InvariantCulture;
            switch (type)
            {
                case SqlDbType.Char:
                case SqlDbType.VarChar:
                case SqlDbType.NChar:
                case SqlDbType.NVarChar:
                    parameter.Size = metadata.MaxLength ?? -1;
                    if (metadata.MaxLength is not int length || query.Length <= length)
                        value = query;
                    break;
                case SqlDbType.TinyInt:
                    if (byte.TryParse(query, NumberStyles.Integer, invariant, out var tiny)) value = tiny;
                    break;
                case SqlDbType.SmallInt:
                    if (short.TryParse(query, NumberStyles.Integer, invariant, out var small)) value = small;
                    break;
                case SqlDbType.Int:
                    if (int.TryParse(query, NumberStyles.Integer, invariant, out var integer)) value = integer;
                    break;
                case SqlDbType.BigInt:
                    if (long.TryParse(query, NumberStyles.Integer, invariant, out var big)) value = big;
                    break;
                case SqlDbType.Decimal:
                    var specification = metadata.SqlType.Split('(', ')')[1].Split(',');
                    parameter.Precision = byte.Parse(specification[0], invariant);
                    parameter.Scale = byte.Parse(specification[1], invariant);
                    if (decimal.TryParse(query, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        invariant, out var number) && decimal.Round(number, Math.Min(parameter.Scale, (byte)28)) == number)
                    {
                        var whole = decimal.Truncate(number).ToString("0", invariant).TrimStart('-');
                        var digits = whole == "0" ? 0 : whole.Length;
                        if (number == 0 || digits <= parameter.Precision - parameter.Scale)
                            value = number;
                    }
                    break;
                case SqlDbType.Float:
                    if (double.TryParse(query, NumberStyles.Float, invariant, out var floating) && double.IsFinite(floating)) value = floating;
                    break;
                case SqlDbType.Real:
                    if (float.TryParse(query, NumberStyles.Float, invariant, out var real) && float.IsFinite(real)) value = real;
                    break;
                case SqlDbType.Money:
                case SqlDbType.SmallMoney:
                    if (decimal.TryParse(query, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        invariant, out var money) && decimal.Round(money, 4) == money &&
                        (type == SqlDbType.Money ? money >= -922337203685477.5808m && money <= 922337203685477.5807m
                            : money >= -214748.3648m && money <= 214748.3647m)) value = money;
                    break;
                case SqlDbType.Bit:
                    if (query == "0") value = false;
                    else if (query == "1") value = true;
                    else if (bool.TryParse(query, out var bit)) value = bit;
                    break;
                case SqlDbType.UniqueIdentifier:
                    if (Guid.TryParse(query, out var guid)) value = guid;
                    break;
            }
            // An identifier that cannot fit a numeric/text column should not
            // cause a conversion error or match a truncated/rounded value.
            parameter.Value = value ?? DBNull.Value;
            return parameter;
        }

        private SqlCommand CreateParentCommand(SqlConnection connection, string sql, string query)
        {
            var command = new SqlCommand(sql, connection) { CommandTimeout = ParentCommandTimeoutSeconds };
            command.Parameters.Add(new SqlParameter("@Query", SqlDbType.NVarChar, 250) { Value = query });
            foreach (var parameter in _parentQueryParameters)
                command.Parameters.Add(parameter);
            _parentQueryParameters.Clear();
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
