#nullable enable

using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;

namespace SmartGridSuite.Api.Services
{
    public sealed class DailyAssignmentRolloverEmailService
    {
        private const string SystemActor =
            "Automatic Daily Rollover";

        private static readonly HashSet<string> HandledStatuses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Sent",
                "DryRun",
                "Skipped"
            };

        private readonly SmartGridDbContext _db;
        private readonly EmailService _emailService;

        private readonly DailyAssignmentEmailSequenceService
            _emailSequence;

        private readonly ILogger<
            DailyAssignmentRolloverEmailService> _logger;

        public DailyAssignmentRolloverEmailService(
            SmartGridDbContext db,
            EmailService emailService,
            DailyAssignmentEmailSequenceService emailSequence,
            ILogger<DailyAssignmentRolloverEmailService> logger)
        {
            _db = db;
            _emailService = emailService;
            _emailSequence = emailSequence;
            _logger = logger;
        }

        public async Task<bool> SendPendingAsync(
            DateTime workDate,
            CancellationToken ct = default)
        {
            workDate = workDate.Date;

            /*
             * Automatic rollover snapshots use SystemActor as
             * PublishedBy. Find the latest automatic snapshot
             * for each field-visible route.
             */
            var automaticRows =
                await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.PublishedBy == SystemActor)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToListAsync(ct);

            if (automaticRows.Count == 0)
                return true;

            var snapshots = automaticRows
                .GroupBy(BuildTargetKey)
                .Where(group =>
                    !string.IsNullOrWhiteSpace(group.Key))
                .Select(group =>
                {
                    var latestVersion =
                        group.Max(x =>
                            x.PublishedVersion);

                    var rows = group
                        .Where(x =>
                            x.PublishedVersion ==
                            latestVersion)
                        .OrderBy(x =>
                            x.SortOrder)
                        .ThenBy(x =>
                            x.Id)
                        .ToList();

                    var first = rows.First();

                    return new TargetSnapshot
                    {
                        TargetType =
                            NormalizeTargetType(
                                first.TargetType),

                        TruckId = rows
                            .Where(x =>
                                x.TruckId.HasValue)
                            .Select(x =>
                                x.TruckId)
                            .FirstOrDefault(),

                        TechnicianId =
                            first.TechnicianId,

                        PublishedVersion =
                            latestVersion,

                        PublishedAt = rows
                            .Max(x =>
                                x.PublishedAt),

                        Rows =
                            rows
                    };
                })
                .ToList();

            var technicians =
                await _db.Technicians
                    .AsNoTracking()
                    .Where(x =>
                        x.IsActive)
                    .ToListAsync(ct);

            var techniciansById =
                technicians.ToDictionary(
                    x => x.Id);

            var trucks =
                await _db.Trucks
                    .AsNoTracking()
                    .Where(x =>
                        x.IsActive)
                    .ToListAsync(ct);

            var trucksById =
                trucks.ToDictionary(
                    x => x.Id);

            var rosterRows =
                await _db.TruckRosters
                    .AsNoTracking()
                    .Where(x =>
                        x.WorkDate == workDate)
                    .ToListAsync(ct);

            var rosterNamesByTruckId = rosterRows
                .Where(row =>
                    techniciansById.ContainsKey(
                        row.TechnicianId))
                .GroupBy(row =>
                    row.TruckId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row =>
                            FormatTechnicianName(
                                techniciansById[
                                    row.TechnicianId]))
                        .Where(name =>
                            !string.IsNullOrWhiteSpace(
                                name))
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name =>
                            name)
                        .ToList());

            var rosterEmailsByTruckId = rosterRows
                .Where(row =>
                    techniciansById.ContainsKey(
                        row.TechnicianId))
                .GroupBy(row =>
                    row.TruckId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row =>
                            techniciansById[
                                row.TechnicianId]
                                .EmailAddress)
                        .Where(address =>
                            !string.IsNullOrWhiteSpace(
                                address))
                        .Select(address =>
                            address!.Trim())
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .ToList());

            var allEmailsAddress =
                await GetAllEmailsAddressAsync(ct);

            var allHandled = true;

            foreach (var snapshot in snapshots)
            {
                try
                {
                    var targetDisplay =
                        BuildTargetDisplay(
                            snapshot,
                            techniciansById,
                            trucksById,
                            rosterNamesByTruckId);

                    var alreadyHandled =
                        await WasAlreadyHandledAsync(
                            targetDisplay,
                            workDate,
                            ct);

                    if (alreadyHandled)
                        continue;

                    var targetQuery =
                        BuildTargetQuery(
                            workDate,
                            snapshot);

                    /*
                     * An automatic rollover email is appropriate only
                     * when Dispatch deliberately published a route for
                     * this date before rollover occurred.
                     *
                     * Carry-only routes remain available in the app,
                     * but do not generate unsolicited emails.
                     */
                    var hadPreviouslyPublishedPlannedRoute =
                        await targetQuery.AnyAsync(
                            x =>
                                x.PublishedVersion <
                                    snapshot.PublishedVersion &&
                                x.PublishedBy !=
                                    SystemActor,
                            ct);

                    if (!hadPreviouslyPublishedPlannedRoute)
                    {
                        _logger.LogInformation(
                            "Automatic rollover email suppressed " +
                            "for {TargetDisplay} on {WorkDate}. " +
                            "No dispatcher-published route existed " +
                            "before rollover.",
                            targetDisplay,
                            workDate);

                        continue;
                    }

                    var previousVersion =
                        await targetQuery
                            .Where(x =>
                                x.PublishedVersion <
                                snapshot.PublishedVersion)
                            .Select(x =>
                                (int?)x.PublishedVersion)
                            .MaxAsync(ct);

                    var previousRows =
                        previousVersion.HasValue
                            ? await targetQuery
                                .Where(x =>
                                    x.PublishedVersion ==
                                    previousVersion.Value)
                                .OrderBy(x =>
                                    x.SortOrder)
                                .ThenBy(x =>
                                    x.Id)
                                .ToListAsync(ct)
                            : new List<
                                DailyTicketAssignmentPublishedEntity>();

                    var ticketIds = previousRows
                        .Select(x =>
                            x.TicketId)
                        .Concat(
                            snapshot.Rows.Select(x =>
                                x.TicketId))
                        .Distinct()
                        .ToList();

                    var ticketsById =
                        await _db.Tickets
                            .AsNoTracking()
                            .Where(x =>
                                ticketIds.Contains(x.Id))
                            .ToDictionaryAsync(
                                x => x.Id,
                                ct);

                    var sequence =
                        await _emailSequence
                            .GetNextAsync(
                                targetDisplay,
                                workDate,
                                ct);

                    var changeSummaryHtml =
                        DailyAssignmentEmailRenderer
                            .BuildDailyAssignmentChangeSummaryHtml(
                                previousRows,
                                snapshot.Rows,
                                ticketsById);

                    var truckNumberDisplay =
                        ResolveTruckNumberDisplay(
                            snapshot.TruckId,
                            trucksById);

                    var body =
                        DailyAssignmentEmailRenderer
                            .BuildDailyAssignmentPublishedEmailBody(
                                workDate,
                                targetDisplay,
                                truckNumberDisplay,
                                SystemActor,
                                snapshot.PublishedAt,
                                sequence.Title,
                                changeSummaryHtml,
                                snapshot.Rows,
                                ticketsById);

                    var subject =
                        $"{targetDisplay} - " +
                        $"{sequence.Title} - " +
                        $"{workDate:MM/dd/yyyy}";

                    var recipients =
                        ResolveRecipients(
                            snapshot,
                            techniciansById,
                            rosterEmailsByTruckId);

                    var currentTicketIds =
                        snapshot.Rows
                            .Select(x =>
                                x.TicketId)
                            .Distinct()
                            .ToList();

                    TicketEntity? onlyTicket = null;

                    if (currentTicketIds.Count == 1)
                    {
                        ticketsById.TryGetValue(
                            currentTicketIds[0],
                            out onlyTicket);
                    }

                    var emailResult =
                        await _emailService.SendAsync(
                            new EmailSendRequest
                            {
                                EmailType =
                                    "DailyAssignment",

                                ToAddresses =
                                    recipients,

                                CcAddresses =
                                    string.IsNullOrWhiteSpace(
                                        allEmailsAddress)
                                        ? Array.Empty<string>()
                                        : new[]
                                        {
                                            allEmailsAddress
                                        },

                                Subject =
                                    subject,

                                Body =
                                    body,

                                IsHtml =
                                    true,

                                CreatedBy =
                                    SystemActor,

                                RelatedTicketId =
                                    currentTicketIds.Count == 1
                                        ? currentTicketIds[0]
                                        : null,

                                RelatedSite =
                                    onlyTicket?.Site
                            },
                            ct);

                    if (!HandledStatuses.Contains(
                            emailResult.Status))
                    {
                        allHandled = false;

                        _logger.LogWarning(
                            "Automatic rollover email was not " +
                            "completed for {TargetDisplay} on " +
                            "{WorkDate}. Status={Status}, " +
                            "Message={Message}",
                            targetDisplay,
                            workDate,
                            emailResult.Status,
                            emailResult.Message);

                        continue;
                    }

                    _logger.LogInformation(
                        "Automatic rollover email handled for " +
                        "{TargetDisplay} on {WorkDate}. " +
                        "Status={Status}, EmailLogId={EmailLogId}.",
                        targetDisplay,
                        workDate,
                        emailResult.Status,
                        emailResult.LogId);
                }
                catch (Exception ex)
                {
                    allHandled = false;

                    _logger.LogError(
                        ex,
                        "Automatic rollover email failed for " +
                        "{TargetType}, TruckId={TruckId}, " +
                        "TechnicianId={TechnicianId} on " +
                        "{WorkDate}.",
                        snapshot.TargetType,
                        snapshot.TruckId,
                        snapshot.TechnicianId,
                        workDate);
                }
            }

            return allHandled;
        }

        private IQueryable<
            DailyTicketAssignmentPublishedEntity>
            BuildTargetQuery(
                DateTime workDate,
                TargetSnapshot snapshot)
        {
            var query =
                _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.TargetType ==
                        snapshot.TargetType);

            if (snapshot.TargetType == "Technician")
            {
                return query.Where(x =>
                    x.TechnicianId ==
                    snapshot.TechnicianId);
            }

            return query.Where(x =>
                x.TruckId ==
                snapshot.TruckId);
        }

        private async Task<bool> WasAlreadyHandledAsync(
            string targetDisplay,
            DateTime workDate,
            CancellationToken ct)
        {
            var subjectPrefix =
                $"{targetDisplay} - ";

            var subjectSuffix =
                $" - {workDate:MM/dd/yyyy}";

            return await _db.EmailLogs
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.EmailType ==
                        "DailyAssignment" &&
                        x.CreatedBy ==
                        SystemActor &&
                        (
                            x.Status == "Sent" ||
                            x.Status == "DryRun" ||
                            x.Status == "Skipped"
                        ) &&
                        x.Subject.StartsWith(
                            subjectPrefix) &&
                        x.Subject.EndsWith(
                            subjectSuffix),
                    ct);
        }

        private static List<string> ResolveRecipients(
            TargetSnapshot snapshot,
            IReadOnlyDictionary<
                uint,
                TechnicianEntity> techniciansById,
            IReadOnlyDictionary<
                uint,
                List<string>> rosterEmailsByTruckId)
        {
            var recipients =
                new List<string>();

            if (snapshot.TruckId.HasValue &&
                rosterEmailsByTruckId.TryGetValue(
                    snapshot.TruckId.Value,
                    out var rosterEmails))
            {
                recipients.AddRange(
                    rosterEmails);
            }

            if (recipients.Count == 0 &&
                snapshot.TechnicianId.HasValue &&
                techniciansById.TryGetValue(
                    snapshot.TechnicianId.Value,
                    out var technician) &&
                !string.IsNullOrWhiteSpace(
                    technician.EmailAddress))
            {
                recipients.Add(
                    technician.EmailAddress.Trim());
            }

            return recipients
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildTargetDisplay(
            TargetSnapshot snapshot,
            IReadOnlyDictionary<
                uint,
                TechnicianEntity> techniciansById,
            IReadOnlyDictionary<
                uint,
                TruckEntity> trucksById,
            IReadOnlyDictionary<
                uint,
                List<string>> rosterNamesByTruckId)
        {
            if (snapshot.TruckId.HasValue &&
                rosterNamesByTruckId.TryGetValue(
                    snapshot.TruckId.Value,
                    out var names) &&
                names.Count > 0)
            {
                return FormatCrewDisplayText(
                    names);
            }

            if (snapshot.TechnicianId.HasValue &&
                techniciansById.TryGetValue(
                    snapshot.TechnicianId.Value,
                    out var technician))
            {
                return FormatTechnicianName(
                    technician);
            }

            if (snapshot.TruckId.HasValue &&
                trucksById.TryGetValue(
                    snapshot.TruckId.Value,
                    out var truck))
            {
                var truckNumber =
                    (truck.TruckNumber ??
                     string.Empty).Trim();

                return string.IsNullOrWhiteSpace(
                    truckNumber)
                    ? "Truck"
                    : $"Truck {truckNumber}";
            }

            return "Daily Assignment Target";
        }

        private static string ResolveTruckNumberDisplay(
            uint? truckId,
            IReadOnlyDictionary<
                uint,
                TruckEntity> trucksById)
        {
            if (!truckId.HasValue ||
                !trucksById.TryGetValue(
                    truckId.Value,
                    out var truck))
            {
                return string.Empty;
            }

            var truckNumber =
                (truck.TruckNumber ??
                 string.Empty).Trim();

            return string.IsNullOrWhiteSpace(
                truckNumber)
                ? string.Empty
                : $"Truck {truckNumber}";
        }

        private async Task<string>
            GetAllEmailsAddressAsync(
                CancellationToken ct)
        {
            var value =
                await _db.AppSettings
                    .AsNoTracking()
                    .Where(x =>
                        x.SettingKey ==
                        "Email.AllEmailsAddress")
                    .Select(x =>
                        x.SettingValue)
                    .FirstOrDefaultAsync(ct);

            return (value ??
                    string.Empty).Trim();
        }

        private static string BuildTargetKey(
            DailyTicketAssignmentPublishedEntity row)
        {
            var targetType =
                NormalizeTargetType(
                    row.TargetType);

            if (targetType == "Technician" &&
                row.TechnicianId.HasValue)
            {
                return
                    $"Technician:" +
                    $"{row.TechnicianId.Value}";
            }

            if (targetType == "Truck" &&
                row.TruckId.HasValue)
            {
                return
                    $"Truck:{row.TruckId.Value}";
            }

            return string.Empty;
        }

        private static string NormalizeTargetType(
            string? targetType)
        {
            var value =
                (targetType ??
                 string.Empty).Trim();

            if (value.Equals(
                    "Technician",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Technician";
            }

            if (value.Equals(
                    "Truck",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Truck";
            }

            return string.Empty;
        }

        private static string FormatTechnicianName(
            TechnicianEntity technician)
        {
            var name =
                $"{technician.FirstName} " +
                $"{technician.LastName}";

            name = name.Trim();

            return string.IsNullOrWhiteSpace(name)
                ? technician.EmployeeId.Trim()
                : name;
        }

        private static string FormatCrewDisplayText(
            IReadOnlyList<string> names)
        {
            var cleanNames = names
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Select(x =>
                    x.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanNames.Count == 0)
                return "Unknown";

            if (cleanNames.Count == 1)
                return cleanNames[0];

            if (cleanNames.Count == 2)
            {
                return
                    $"{cleanNames[0]} & " +
                    $"{cleanNames[1]}";
            }

            return string.Join(
                       ", ",
                       cleanNames.Take(
                           cleanNames.Count - 1)) +
                   " & " +
                   cleanNames.Last();
        }

        private sealed class TargetSnapshot
        {
            public string TargetType { get; init; } =
                string.Empty;

            public uint? TruckId { get; init; }

            public uint? TechnicianId { get; init; }

            public int PublishedVersion { get; init; }

            public DateTime PublishedAt { get; init; }

            public IReadOnlyList<
                DailyTicketAssignmentPublishedEntity> Rows
            { get; init; } =
                Array.Empty<
                    DailyTicketAssignmentPublishedEntity>();
        }
    }
}