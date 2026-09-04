#nullable enable

using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;

namespace SmartGridSuite.Api.Services
{
    public sealed class DailyAssignmentRolloverResult
    {
        public DateTime WorkDate { get; init; }

        public int CarriedCount { get; init; }

        public int WithdrawnCount { get; init; }

        public int? PublishedVersion { get; init; }

        public IReadOnlyList<DailyAssignmentRolloverTargetResult> ChangedTargets { get; init; } =
            Array.Empty<DailyAssignmentRolloverTargetResult>();

        public bool HasChanges => CarriedCount > 0 || WithdrawnCount > 0;

        public static DailyAssignmentRolloverResult NoChanges(
            DateTime workDate,
            int withdrawnCount = 0)
        {
            return new DailyAssignmentRolloverResult
            {
                WorkDate = workDate.Date,
                WithdrawnCount = withdrawnCount
            };
        }

        public bool EmailDeliveryPending { get; set; }
    }

    public sealed class DailyAssignmentRolloverTargetResult
    {
        public string TargetType { get; init; } =
            string.Empty;

        public uint? TruckId { get; init; }

        public uint? TechnicianId { get; init; }

        public uint? CrewId { get; init; }

        public IReadOnlyList<long> CarriedTicketIds
        { get; init; } =
            Array.Empty<long>();
    }
    public sealed class DailyAssignmentRolloverService
    {
        private const string AssignmentStatusActive = "Active";
        private const string AssignmentStatusRemoved = "Removed";
        private const string SystemActor = "Automatic Daily Rollover";
        public static TimeSpan ScheduledRunTime { get; } = new(5, 0, 0);

        /*
         * Field Tech Tasks and Daily Assignments can load simultaneously.
         * Only one request may perform rollover inside this API process.
         */
        private static readonly SemaphoreSlim RolloverLock = new(1, 1);

        private static DateTime? _completedWorkDate;

        private static DateTime? _pendingEmailWorkDate;

        private readonly SmartGridDbContext _db;

        private readonly ILogger<DailyAssignmentRolloverService> _logger;

        private readonly DailyAssignmentRolloverEmailService _rolloverEmail;

        public DailyAssignmentRolloverService(
            SmartGridDbContext db,
            DailyAssignmentRolloverEmailService rolloverEmail,
            ILogger<DailyAssignmentRolloverService> logger)
        {
            _db = db;
            _rolloverEmail = rolloverEmail;
            _logger = logger;
        }

        public async Task<DailyAssignmentRolloverResult> EnsureCurrentDayRolloverAsync(
            DateTime workDate,
            CancellationToken ct = default)
        {
            workDate = workDate.Date;

            /*
             * Looking at another date must never pull work into
             * Daily Assignments early.
             */
            if (workDate != DateTime.Today.Date)
            {
                return DailyAssignmentRolloverResult
                    .NoChanges(workDate);
            }

            /*
             * Morning rollover occurs at 5:00 AM server-local
             * time.
             */
            if (DateTime.Now.TimeOfDay <
                ScheduledRunTime)
            {
                return DailyAssignmentRolloverResult
                    .NoChanges(workDate);
            }

            /*
             * A completed rollover can return immediately unless
             * an email attempt still needs to be retried.
             */
            if (_completedWorkDate == workDate &&
                _pendingEmailWorkDate != workDate)
            {
                return DailyAssignmentRolloverResult
                    .NoChanges(workDate);
            }

            await RolloverLock.WaitAsync(ct);

            try
            {
                DailyAssignmentRolloverResult result;

                if (_completedWorkDate != workDate)
                {
                    result =
                        await PerformRolloverAsync(
                            workDate,
                            ct);

                    _completedWorkDate = workDate;

                    if (result.ChangedTargets.Count > 0)
                    {
                        _pendingEmailWorkDate =
                            workDate;
                    }
                }
                else
                {
                    /*
                     * The database work already succeeded. This
                     * pass exists only to retry the email.
                     */
                    result =
                        DailyAssignmentRolloverResult
                            .NoChanges(workDate);
                }

                if (_pendingEmailWorkDate == workDate)
                {
                    var emailsHandled =
                        await _rolloverEmail
                            .SendPendingAsync(
                                workDate,
                                ct);

                    if (emailsHandled)
                    {
                        _pendingEmailWorkDate = null;
                    }
                    else
                    {
                        result.EmailDeliveryPending = true;

                        _logger.LogWarning(
                            "One or more automatic rollover " +
                            "emails remain pending for " +
                            "{WorkDate}.",
                            workDate);
                    }
                }

                return result;
            }
            finally
            {
                RolloverLock.Release();
            }
        }

        private async Task<DailyAssignmentRolloverResult>PerformRolloverAsync(
            DateTime workDate,
            CancellationToken ct)
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(ct);

            try
            {
                var now = DateTime.Now;

                var statusRows = await _db.TicketStatuses
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .Select(x => new
                    {
                        x.Name,
                        x.IsClosed,
                        x.IsFieldComplete
                    })
                    .ToListAsync(ct);

                var closedStatuses = statusRows
                    .Where(x => x.IsClosed)
                    .Select(x => x.Name)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

                var fieldCompleteStatuses = statusRows
                    .Where(x => x.IsFieldComplete)
                    .Select(x => x.Name)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

                /*
                 * Load every row for today, including Removed and
                 * Completed rows. Any existing row is an explicit
                 * decision for this date and suppresses another copy.
                 */
                var destinationRows =
                    await _db.DailyTicketAssignments
                        .Include(x => x.Ticket)
                        .Where(x =>
                            x.AssignmentDate == workDate)
                        .ToListAsync(ct);

                /*
                 * A ticket may have been scheduled for today several
                 * days ago and then completed before today arrived.
                 * Withdraw that stale future assignment.
                 */
                var staleDestinationRows = destinationRows
                    .Where(x =>
                        x.AssignmentStatus ==
                            AssignmentStatusActive &&
                        x.Ticket != null &&
                        IsFinished(
                            x.Ticket,
                            closedStatuses,
                            fieldCompleteStatuses))
                    .ToList();

                foreach (var stale in staleDestinationRows)
                {
                    stale.AssignmentStatus =
                        AssignmentStatusRemoved;

                    stale.IsPublished = false;
                    stale.RemovedAt = now;
                    stale.RemovedBy = SystemActor;
                    stale.UpdatedAt = now;
                    stale.UpdatedBy = SystemActor;
                }

                var destinationTicketIds = destinationRows
                    .Select(x => x.TicketId)
                    .ToHashSet();

                /*
                 * Published snapshots are what technicians actually
                 * received. Locate the newest prior published state
                 * for every ticket.
                 */
                var priorPublishedRows =
                    await _db.DailyTicketAssignmentPublished
                        .AsNoTracking()
                        .Include(x => x.Ticket)
                        .Include(x => x.SourceAssignment)
                        .Where(x =>
                            x.AssignmentDate < workDate &&
                            x.SourceAssignment != null &&
                            x.SourceAssignment.AssignmentStatus == AssignmentStatusActive &&
                                !destinationTicketIds.Contains(x.TicketId))
                        .OrderByDescending(x =>
                            x.AssignmentDate)
                        .ThenByDescending(x =>
                            x.PublishedVersion)
                        .ThenByDescending(x =>
                            x.PublishedAt)
                        .ThenByDescending(x => x.Id)
                        .ToListAsync(ct);

                var latestPriorRows = priorPublishedRows
                    .GroupBy(x => x.TicketId)
                    .Select(x => x.First())
                    .Where(IsActionablePublishedRow)
                    .Where(x =>
                        x.Ticket != null &&
                        !IsFinished(
                            x.Ticket,
                            closedStatuses,
                            fieldCompleteStatuses))
                    .Where(x =>
                        !destinationTicketIds.Contains(
                            x.TicketId))
                    .ToList();

                if (latestPriorRows.Count == 0)
                {
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);

                    if (staleDestinationRows.Count > 0)
                    {
                        _logger.LogInformation(
                            "Automatic rollover withdrew " +
                            "{WithdrawnCount} completed future " +
                            "assignment(s) for {WorkDate}.",
                            staleDestinationRows.Count,
                            workDate);
                    }

                    return DailyAssignmentRolloverResult
                        .NoChanges(
                            workDate,
                            staleDestinationRows.Count);
                }

                var activeTechnicians =
                    await _db.Technicians
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .ToListAsync(ct);

                var activeTechniciansById =
                    activeTechnicians.ToDictionary(x => x.Id);

                var activeTrucks =
                    await _db.Trucks
                        .AsNoTracking()
                        .Where(x => x.IsActive)
                        .ToListAsync(ct);

                var activeTrucksById =
                    activeTrucks.ToDictionary(x => x.Id);

                var truckIdByNumber = activeTrucks
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.TruckNumber))
                    .GroupBy(
                        x => x.TruckNumber.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First().Id,
                        StringComparer.OrdinalIgnoreCase);

                var rosterRows =
                    await _db.TruckRosters
                        .AsNoTracking()
                        .Where(x => x.WorkDate == workDate)
                        .ToListAsync(ct);

                var truckByTechnicianId = rosterRows
                    .GroupBy(x => x.TechnicianId)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First().TruckId);

                var crews = await _db.Crews
                    .AsNoTracking()
                    .Where(x => x.WorkDate == workDate)
                    .ToListAsync(ct);

                var crewByTruckId = crews
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.TruckNumber) &&
                        truckIdByNumber.ContainsKey(
                            x.TruckNumber.Trim()))
                    .GroupBy(x =>
                        truckIdByNumber[
                            x.TruckNumber!.Trim()])
                    .ToDictionary(
                        x => x.Key,
                        x => x.OrderBy(c => c.Id).First());

                var rosterNamesByTruckId = rosterRows
                    .Where(x =>
                        activeTechniciansById.ContainsKey(
                            x.TechnicianId))
                    .GroupBy(x => x.TruckId)
                    .ToDictionary(
                        x => x.Key,
                        x => x
                            .Select(row =>
                                FormatTechnicianName(
                                    activeTechniciansById[
                                        row.TechnicianId]))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .OrderBy(name => name)
                            .ToList());

                var candidates = latestPriorRows
                    .Select(source => new RolloverCandidate
                    {
                        Source = source,
                        Target = ResolveCurrentTarget(
                            source.SourceAssignment!,
                            activeTechniciansById,
                            activeTrucksById,
                            truckByTechnicianId,
                            crewByTruckId)
                    })
                    .Where(x => x.Target != null)
                    .Select(x =>
                    {
                        x.Target = x.Target!;
                        return x;
                    })
                    .ToList();

                /*
                 * The database permits only one active Technician assignment
                 * for the same ticket. Rollover therefore closes yesterday's
                 * active lifecycle row before creating today's replacement.
                 *
                 * The immutable published record remains available for audit,
                 * while CarriedFromAssignmentId connects today's row to it.
                 */
                var sourceAssignmentIds = candidates
                    .Where(x =>
                        x.Source.SourceAssignmentId.HasValue)
                    .Select(x =>
                        x.Source.SourceAssignmentId!.Value)
                    .Distinct()
                    .ToList();

                var sourceAssignmentsToAdvance =
                    await _db.DailyTicketAssignments
                        .Where(x =>
                            sourceAssignmentIds.Contains(x.Id) &&
                            x.AssignmentStatus ==
                                AssignmentStatusActive)
                        .ToListAsync(ct);

                var activeSourceAssignmentIds =
                    sourceAssignmentsToAdvance
                        .Select(x => x.Id)
                        .ToHashSet();

                /*
                 * If another request completed or removed a source assignment
                 * after candidate discovery, it must not be carried.
                 */
                candidates = candidates
                    .Where(x =>
                        x.Source.SourceAssignmentId.HasValue &&
                        activeSourceAssignmentIds.Contains(
                            x.Source.SourceAssignmentId.Value))
                    .ToList();

                if (candidates.Count == 0)
                {
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);

                    return DailyAssignmentRolloverResult
                        .NoChanges(
                            workDate,
                            staleDestinationRows.Count);
                }

                foreach (var sourceAssignment in
                         sourceAssignmentsToAdvance)
                {
                    if (!candidates.Any(x =>
                            x.Source.SourceAssignmentId ==
                                sourceAssignment.Id))
                    {
                        continue;
                    }

                    sourceAssignment.AssignmentStatus =
                        AssignmentStatusRemoved;

                    sourceAssignment.RemovedAt = now;
                    sourceAssignment.RemovedBy = SystemActor;

                    sourceAssignment.UpdatedAt = now;
                    sourceAssignment.UpdatedBy = SystemActor;
                }

                /*
                 * Release the active-ticket unique keys before inserting the
                 * new current-day rows. This save is still inside the transaction
                 * and will be rolled back if any later operation fails.
                 */
                await _db.SaveChangesAsync(ct);

                var nextPublishedVersion =
                    (await _db
                        .DailyTicketAssignmentPublished
                        .AsNoTracking()
                        .Where(x =>
                            x.AssignmentDate == workDate)
                        .Select(x =>
                            (int?)x.PublishedVersion)
                        .MaxAsync(ct) ?? 0) + 1;

                var candidateGroups = candidates
                    .GroupBy(x => x.Target!.Key)
                    .ToList();

                /*
                 * Capture the route that technicians could already
                 * see before creating the replacement snapshots.
                 */
                var existingPublishedByTarget =
                    new Dictionary<
                        string,
                        List<DailyTicketAssignmentPublishedEntity>>();

                foreach (var group in candidateGroups)
                {
                    existingPublishedByTarget[group.Key] =
                        await LoadCurrentPublishedTargetAsync(
                            workDate,
                            group.First().Target!,
                            closedStatuses,
                            fieldCompleteStatuses,
                            ct);
                }

                foreach (var group in candidateGroups)
                {
                    var target = group.First().Target!;

                    var orderedCarryRows = group
                        .OrderByDescending(x =>
                            x.Source.AssignmentDate)
                        .ThenBy(x =>
                            x.Source.SortOrder)
                        .ThenBy(x =>
                            x.Source.Id)
                        .ToList();

                    var shiftAmount =
                        orderedCarryRows.Count * 10;

                    var existingTargetRows = destinationRows
                        .Where(x =>
                            x.AssignmentStatus ==
                                AssignmentStatusActive &&
                            MatchesTarget(x, target))
                        .ToList();

                    /*
                     * Rollover work always appears first. Existing
                     * planned work keeps its relative order below it.
                     */
                    foreach (var existing in existingTargetRows)
                    {
                        existing.SortOrder += shiftAmount;
                        existing.UpdatedAt = now;
                        existing.UpdatedBy = SystemActor;
                    }

                    var sortOrder = 0;

                    foreach (var candidate in orderedCarryRows)
                    {
                        sortOrder += 10;

                        var sourceAssignment =
                            candidate.Source.SourceAssignment!;

                        var assignment =
                            new DailyTicketAssignmentEntity
                            {
                                AssignmentDate = workDate,
                                TicketId =
                                    candidate.Source.TicketId,

                                TargetType =
                                    target.TargetType,

                                TruckId =
                                    target.TruckId,

                                TechnicianId =
                                    target.TechnicianId,

                                CrewId =
                                    target.CrewId,

                                SortOrder = sortOrder,

                                IsPublished = true,

                                PublishedVersion =
                                    nextPublishedVersion,

                                PublishedAt = now,
                                PublishedBy = SystemActor,

                                CarriedFromAssignmentId =
                                    sourceAssignment.Id,

                                AssignmentNotes =
                                    candidate.Source
                                        .AssignmentNotes,

                                AssignmentStatus =
                                    AssignmentStatusActive,

                                CreatedAt = now,
                                CreatedBy = SystemActor,
                                UpdatedAt = now,
                                UpdatedBy = SystemActor
                            };

                        candidate.CreatedAssignment =
                            assignment;

                        _db.DailyTicketAssignments.Add(
                            assignment);
                    }
                }

                /*
                 * Save once so carried assignments receive their IDs
                 * before immutable publication rows reference them.
                 */
                await _db.SaveChangesAsync(ct);

                var carriedTicketIds = candidates
                    .Select(x => x.Source.TicketId)
                    .Distinct()
                    .ToList();

                var ticketsById = await _db.Tickets
                    .Where(x =>
                        carriedTicketIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct);

                var newPublishedRows =
                    new List<
                        DailyTicketAssignmentPublishedEntity>();

                foreach (var group in candidateGroups)
                {
                    var target = group.First().Target!;

                    var orderedCarryRows = group
                        .OrderByDescending(x =>
                            x.Source.AssignmentDate)
                        .ThenBy(x =>
                            x.Source.SortOrder)
                        .ThenBy(x =>
                            x.Source.Id)
                        .ToList();

                    var publishedSortOrder = 0;

                    foreach (var candidate in orderedCarryRows)
                    {
                        publishedSortOrder += 10;

                        var assignment =
                            candidate.CreatedAssignment!;

                        assignment.SortOrder =
                            publishedSortOrder;

                        newPublishedRows.Add(
                            CreatePublishedRow(
                                assignment,
                                nextPublishedVersion,
                                publishedSortOrder,
                                now));

                        if (ticketsById.TryGetValue(
                                assignment.TicketId,
                                out var ticket))
                        {
                            ticket.AssignedTech =
                                BuildAssignedToText(
                                    target,
                                    activeTechniciansById,
                                    activeTrucksById,
                                    rosterNamesByTruckId);

                            ticket.AssignedCrewId =
                                target.CrewId;

                            ticket.LastActivityAt = now;
                        }
                    }

                    /*
                     * Copy the previously published planned route
                     * underneath the rollover tickets. Unpublished
                     * dispatcher drafts remain unpublished.
                     */
                    foreach (var existing in
                             existingPublishedByTarget[group.Key])
                    {
                        publishedSortOrder += 10;

                        newPublishedRows.Add(
                            new DailyTicketAssignmentPublishedEntity
                            {
                                AssignmentDate = workDate,

                                PublishedVersion =
                                    nextPublishedVersion,

                                TicketId = existing.TicketId,

                                SourceAssignmentId =
                                    existing.SourceAssignmentId,

                                TargetType =
                                    existing.TargetType,

                                TruckId = existing.TruckId,

                                TechnicianId =
                                    existing.TechnicianId,

                                CrewId = existing.CrewId,

                                SortOrder =
                                    publishedSortOrder,

                                AssignmentNotes =
                                    existing.AssignmentNotes,

                                PublishedAt = now,
                                PublishedBy = SystemActor
                            });
                    }
                }

                _db.DailyTicketAssignmentPublished.AddRange(
                    newPublishedRows);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                _logger.LogInformation(
                    "Automatic Daily Assignment rollover for " +
                    "{WorkDate} carried {CarriedCount} ticket(s), " +
                    "withdrew {WithdrawnCount} completed future " +
                    "assignment(s), and published version " +
                    "{PublishedVersion}.",
                    workDate,
                    candidates.Count,
                    staleDestinationRows.Count,
                    nextPublishedVersion);

                var changedTargets = candidateGroups
                    .Select(group =>
                    {
                        var target =
                            group.First().Target!;

                        return new
                            DailyAssignmentRolloverTargetResult
                        {
                            TargetType =
                                target.TargetType,

                            TruckId =
                                target.TruckId,

                            TechnicianId =
                                target.TechnicianId,

                            CrewId =
                                target.CrewId,

                            CarriedTicketIds = group
                                .Select(x =>
                                    x.Source.TicketId)
                                .Distinct()
                                .ToList()
                        };
                    })
                    .ToList();

                return new DailyAssignmentRolloverResult
                {
                    WorkDate = workDate,
                    CarriedCount = candidates.Count,
                    WithdrawnCount =
                        staleDestinationRows.Count,
                    PublishedVersion =
                        nextPublishedVersion,
                    ChangedTargets =
                        changedTargets
                };
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        private async Task<List<DailyTicketAssignmentPublishedEntity>>LoadCurrentPublishedTargetAsync(
            DateTime workDate,
            RolloverTarget target,
            HashSet<string> closedStatuses,
            HashSet<string> fieldCompleteStatuses,
            CancellationToken ct)
        {
            var query =
                _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.TargetType == target.TargetType);

            if (target.TargetType == "Technician")
            {
                query = query.Where(x =>
                    x.TechnicianId ==
                        target.TechnicianId);
            }
            else
            {
                query = query.Where(x =>
                    x.TruckId == target.TruckId);
            }

            var latestVersion = await query
                .Select(x =>
                    (int?)x.PublishedVersion)
                .MaxAsync(ct);

            if (!latestVersion.HasValue)
            {
                return new List<
                    DailyTicketAssignmentPublishedEntity>();
            }

            var rows = await query
                .Include(x => x.Ticket)
                .Include(x => x.SourceAssignment)
                .Where(x =>
                    x.PublishedVersion ==
                        latestVersion.Value)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            return rows
                .Where(IsActionablePublishedRow)
                .Where(x =>
                    x.Ticket != null &&
                    !IsFinished(
                        x.Ticket,
                        closedStatuses,
                        fieldCompleteStatuses))
                .ToList();
        }

        private static RolloverTarget? ResolveCurrentTarget(
            DailyTicketAssignmentEntity source,
            IReadOnlyDictionary<uint, TechnicianEntity> activeTechniciansById,
            IReadOnlyDictionary<uint, TruckEntity> activeTrucksById,
            IReadOnlyDictionary<uint, uint> truckByTechnicianId,
            IReadOnlyDictionary<uint, CrewEntity> crewByTruckId)
        {
            var targetType =
                NormalizeTargetType(source.TargetType);

            if (targetType == "Technician")
            {
                if (!source.TechnicianId.HasValue ||
                    !activeTechniciansById.ContainsKey(
                        source.TechnicianId.Value))
                {
                    return null;
                }

                var sourceTechnicianId =
                    source.TechnicianId.Value;

                if (truckByTechnicianId.TryGetValue(
                        sourceTechnicianId,
                        out var currentTruckId) &&
                    activeTrucksById.ContainsKey(
                        currentTruckId))
                {
                    crewByTruckId.TryGetValue(
                        currentTruckId,
                        out var currentCrew);

                    var routeOwnerId =
                        currentCrew?.LeadTechnicianId;

                    if (!routeOwnerId.HasValue ||
                        !activeTechniciansById.ContainsKey(
                            routeOwnerId.Value))
                    {
                        routeOwnerId =
                            sourceTechnicianId;
                    }

                    return new RolloverTarget
                    {
                        TargetType = "Technician",
                        TruckId = currentTruckId,
                        TechnicianId =
                            routeOwnerId.Value,
                        CrewId = currentCrew?.Id
                    };
                }

                return new RolloverTarget
                {
                    TargetType = "Technician",
                    TruckId = null,
                    TechnicianId =
                        sourceTechnicianId,
                    CrewId = null
                };
            }

            if (targetType == "Truck" &&
                source.TruckId.HasValue &&
                activeTrucksById.ContainsKey(
                    source.TruckId.Value))
            {
                var truckId = source.TruckId.Value;

                crewByTruckId.TryGetValue(
                    truckId,
                    out var crew);

                /*
                 * Modern field routes are owned by the crew lead.
                 * Convert an older Truck route when a current lead
                 * is available.
                 */
                if (crew?.LeadTechnicianId is uint leadId &&
                    activeTechniciansById.ContainsKey(leadId))
                {
                    return new RolloverTarget
                    {
                        TargetType = "Technician",
                        TruckId = truckId,
                        TechnicianId = leadId,
                        CrewId = crew.Id
                    };
                }

                return new RolloverTarget
                {
                    TargetType = "Truck",
                    TruckId = truckId,
                    TechnicianId = null,
                    CrewId = crew?.Id
                };
            }

            return null;
        }

        private static bool IsActionablePublishedRow(DailyTicketAssignmentPublishedEntity row)
        {
            var source = row.SourceAssignment;

            if (source == null ||
                source.AssignmentStatus !=
                    AssignmentStatusActive)
            {
                return false;
            }

            var publishedTarget =
                NormalizeTargetType(row.TargetType);

            var sourceTarget =
                NormalizeTargetType(source.TargetType);

            if (publishedTarget != sourceTarget)
                return false;

            if (publishedTarget == "Technician")
            {
                return row.TechnicianId.HasValue &&
                       row.TechnicianId ==
                           source.TechnicianId;
            }

            if (publishedTarget == "Truck")
            {
                return row.TruckId.HasValue &&
                       row.TruckId ==
                           source.TruckId;
            }

            return false;
        }

        private static bool MatchesTarget(
            DailyTicketAssignmentEntity assignment,
            RolloverTarget target)
        {
            if (NormalizeTargetType(
                    assignment.TargetType) !=
                target.TargetType)
            {
                return false;
            }

            return target.TargetType == "Technician"
                ? assignment.TechnicianId ==
                    target.TechnicianId
                : assignment.TruckId ==
                    target.TruckId;
        }

        private static bool IsFinished(
            TicketEntity ticket,
            HashSet<string> closedStatuses,
            HashSet<string> fieldCompleteStatuses)
        {
            var status =
                ticket.Status ?? string.Empty;

            return closedStatuses.Contains(status) ||
                   fieldCompleteStatuses.Contains(status);
        }

        private static DailyTicketAssignmentPublishedEntity CreatePublishedRow(
            DailyTicketAssignmentEntity assignment,
            int publishedVersion,
            int sortOrder,
            DateTime publishedAt)
        {
            return new
                DailyTicketAssignmentPublishedEntity
            {
                AssignmentDate =
                        assignment.AssignmentDate,

                PublishedVersion =
                        publishedVersion,

                TicketId = assignment.TicketId,

                SourceAssignmentId =
                        assignment.Id,

                TargetType =
                        assignment.TargetType,

                TruckId = assignment.TruckId,

                TechnicianId =
                        assignment.TechnicianId,

                CrewId = assignment.CrewId,

                SortOrder = sortOrder,

                AssignmentNotes =
                        assignment.AssignmentNotes,

                PublishedAt = publishedAt,
                PublishedBy = SystemActor
            };
        }

        private static string BuildAssignedToText(
            RolloverTarget target,
            IReadOnlyDictionary<uint, TechnicianEntity> techniciansById,
            IReadOnlyDictionary<uint, TruckEntity> trucksById,
            IReadOnlyDictionary<uint, List<string>> rosterNamesByTruckId)
        {
            if (target.TruckId.HasValue)
            {
                if (rosterNamesByTruckId.TryGetValue(
                        target.TruckId.Value,
                        out var names) &&
                    names.Count > 0)
                {
                    return FormatCrewDisplayText(names);
                }

                if (target.TechnicianId.HasValue &&
                    techniciansById.TryGetValue(
                        target.TechnicianId.Value,
                        out var lead))
                {
                    return FormatTechnicianName(lead);
                }

                if (trucksById.TryGetValue(
                        target.TruckId.Value,
                        out var truck))
                {
                    return $"Truck " +
                           $"{truck.TruckNumber.Trim()}";
                }
            }

            if (target.TechnicianId.HasValue &&
                techniciansById.TryGetValue(
                    target.TechnicianId.Value,
                    out var technician))
            {
                return FormatTechnicianName(
                    technician);
            }

            return "(Unassigned)";
        }

        private static string FormatTechnicianName(TechnicianEntity technician)
        {
            var name =
                $"{technician.FirstName} " +
                $"{technician.LastName}";

            name = name.Trim();

            return string.IsNullOrWhiteSpace(name)
                ? technician.EmployeeId.Trim()
                : name;
        }

        private static string FormatCrewDisplayText(IReadOnlyList<string> names)
        {
            var cleanNames = names
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
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

        private static string NormalizeTargetType(string? targetType)
        {
            var value =
                (targetType ?? string.Empty).Trim();

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

        private sealed class RolloverCandidate
        {
            public
                DailyTicketAssignmentPublishedEntity
                Source
            { get; set; } = null!;

            public RolloverTarget? Target
            {
                get;
                set;
            }

            public DailyTicketAssignmentEntity?
                CreatedAssignment
            { get; set; }
        }

        private sealed class RolloverTarget
        {
            public string TargetType
            {
                get;
                set;
            } = string.Empty;

            public uint? TruckId { get; set; }

            public uint? TechnicianId
            {
                get;
                set;
            }

            public uint? CrewId { get; set; }

            public string Key =>
                TargetType == "Technician"
                    ? $"Technician:{TechnicianId}"
                    : $"Truck:{TruckId}";
        }
    }
}