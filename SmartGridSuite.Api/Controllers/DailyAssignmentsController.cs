#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Dispatcher.DailyAssignments;
using System.Text;
using System.Net;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/daily-assignments")]
    public sealed class DailyAssignmentsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        private readonly TruckBoardInitializationService _truckBoardInitialization;
        private readonly EmailService _emailService;

        private readonly DailyAssignmentEmailSequenceService
            _dailyAssignmentEmailSequence;

        private readonly ILogger<DailyAssignmentsController> _logger;

        private const string TechnicianRoleCode = "TECHNICIAN";
        private const string LinemanRoleCode = "LINEMAN";

        private const string AssignmentStatusActive = "Active";
        private const string AssignmentStatusCompleted = "Completed";
        private const string AssignmentStatusRemoved = "Removed";

        public DailyAssignmentsController(
            SmartGridDbContext db,
            TruckBoardInitializationService truckBoardInitialization,
            EmailService emailService,
            DailyAssignmentEmailSequenceService dailyAssignmentEmailSequence,
            ILogger<DailyAssignmentsController> logger)
        {
            _db = db;

            _truckBoardInitialization =
                truckBoardInitialization;

            _emailService =
                emailService;

            _dailyAssignmentEmailSequence =
                dailyAssignmentEmailSequence;

            _logger =
                logger;
        }

        [HttpGet("board")]
        public async Task<ActionResult<DailyAssignmentsBoardDto>> GetBoard([FromQuery] string? date = null, CancellationToken ct = default)
        {
            var rosterDate = ParseDateOrToday(date);
            var assignmentDate = rosterDate;

            /*
             * Daily Assignments may be the first dispatch pane opened in the morning.
             * Ensure today's truck roster and crews exist before building assignment targets.
             */
            await _truckBoardInitialization.EnsureBoardInitializedAsync(rosterDate, ct);

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

            var closedStatusNames = statusRows
                .Where(x => x.IsClosed)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fieldCompleteStatusNames = statusRows
                .Where(x => x.IsFieldComplete)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var assignments = await _db.DailyTicketAssignments
                .AsNoTracking()
                .Include(x => x.Ticket)
                    .ThenInclude(t => t!.TaskCategory)
                .Include(x => x.Truck)
                .Include(x => x.Technician)
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.AssignmentStatus == AssignmentStatusActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            /*
             * Closed tickets remain stored in assignment history, but they are no longer
             * active work and must not appear in Dispatcher Daily Assignment lists.
             * Field-complete tickets remain visible because they are not closed.
             */
            var visibleAssignments = assignments
                .Where(x => x.Ticket != null)
                .Where(x =>
                    !closedStatusNames.Contains(
                        x.Ticket!.Status ?? string.Empty))
                .ToList();

            var assignmentByTicketId = visibleAssignments
                .GroupBy(x => x.TicketId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.UpdatedAt).First());

            var nonClosedTickets = await _db.Tickets
                .AsNoTracking()
                .Include(t => t.TaskCategory)
                .Where(t => !closedStatusNames.Contains(t.Status))
                .OrderBy(t => t.PriorityDays == 0 ? 999 : t.PriorityDays)
                .ThenByDescending(t => t.LastActivityAt)
                .ToListAsync(ct);

            var trucks = await _db.Trucks
                .AsNoTracking()
                .Include(t => t.TruckStyle)
                .Where(t => t.IsActive)
                .OrderBy(t => t.TruckNumber)
                .ToListAsync(ct);

            var rosterRows = await (
                from roster in _db.TruckRosters.AsNoTracking()
                join tech in ActiveFieldTechniciansQuery()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == rosterDate
                select new
                {
                    TruckId = roster.TruckId,
                    Technician = tech
                })
                .ToListAsync(ct);

            var truckNumbers = trucks
                .Select(x => x.TruckNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var crews = await _db.Crews
                .AsNoTracking()
                .Where(c =>
                    c.WorkDate == rosterDate &&
                    c.TruckNumber != null &&
                    truckNumbers.Contains(c.TruckNumber))
                .ToListAsync(ct);

            var crewByTruckNumber = crews
                .Select(c => new
                {
                    Crew = c,
                    TruckNumber = (c.TruckNumber ?? string.Empty).Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber))
                .GroupBy(x => x.TruckNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Crew).OrderBy(x => x.Id).First(),
                    StringComparer.OrdinalIgnoreCase);

            var techsByTruckId = rosterRows
                .GroupBy(x => x.TruckId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => MapTechnician(x.Technician, rosterDate, (int?)x.TruckId, null))
                          .OrderBy(x => x.Name)
                          .ToList());            

            var assignedTruckNumberByTechId = rosterRows
                .Join(trucks,
                    r => r.TruckId,
                    t => t.Id,
                    (r, t) => new
                    {
                        TechnicianId = r.Technician.Id,
                        t.TruckNumber
                    })
                .GroupBy(x => x.TechnicianId)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().TruckNumber);

            var allActiveTechs = await ActiveFieldTechniciansQuery()
                .OrderBy(t => t.LastName)
                .ThenBy(t => t.FirstName)
                .ToListAsync(ct);

            var technicianAssignmentsByTechId = visibleAssignments
                .Where(x =>
                    IsTargetType(x.TargetType, "Technician") &&
                    x.TechnicianId.HasValue)
                .GroupBy(x => x.TechnicianId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SortOrder)
                          .ThenBy(x => x.Id)
                          .Select(x => MapAssignedTicket(
                              x,
                              closedStatusNames,
                              fieldCompleteStatusNames))
                          .ToList());

            var rosterTechEntitiesByTruckId = rosterRows
                .GroupBy(x => x.TruckId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Technician)
                          .OrderByDescending(GetTitleRank)
                          .ThenBy(x => x.LastName)
                          .ThenBy(x => x.FirstName)
                          .ToList());

            var truckTargets = trucks
                .Select(truck =>
                {
                    var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

                    CrewEntity? crew = null;

                    if (!string.IsNullOrWhiteSpace(truckNumber))
                        crewByTruckNumber.TryGetValue(truckNumber, out crew);

                    var rosterTechs = rosterTechEntitiesByTruckId.TryGetValue(truck.Id, out var techEntities)
                        ? techEntities
                        : new List<TechnicianEntity>();

                    var leadTech = PickLeadTechnician(
                        rosterTechs,
                        truck.Id,
                        crew?.LeadTechnicianId);

                    if (leadTech == null)
                        return null;

                    var leadTechId = leadTech.Id;

                    return new DailyAssignmentTargetDto
                    {
                        TargetKey = $"CrewLead:{leadTechId}",
                        TargetType = "Technician",

                        // Truck is display context only now.
                        // The assignment owner is TechnicianId / lead tech.
                        TruckId = (int)truck.Id,
                        TruckNumber = truckNumber,
                        TruckStyleName = truck.TruckStyle?.Name,

                        TechnicianId = (int)leadTechId,
                        TechnicianName = FormatTechnicianName(
                            leadTech.FirstName,
                            leadTech.LastName,
                            leadTech.EmployeeId),
                        TechnicianTitle = leadTech.Title,

                        CrewId = crew == null ? null : (int?)crew.Id,

                        Technicians = techsByTruckId.TryGetValue(truck.Id, out var techs)
                            ? techs
                            : new List<DailyAssignmentTechnicianDto>(),

                        AssignedTickets = technicianAssignmentsByTechId.TryGetValue(leadTechId, out var assigned)
                            ? assigned
                            : new List<DailyAssignedTicketDto>()
                    };
                })
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();

            var leadTechnicianIds = truckTargets
                .Where(x => x.TechnicianId.HasValue)
                .Select(x => (uint)x.TechnicianId!.Value)
                .ToHashSet();

            var rosteredTechnicianIds = assignedTruckNumberByTechId.Keys.ToHashSet();

            var technicianTargetIds = allActiveTechs
                .Where(t => !rosteredTechnicianIds.Contains(t.Id))
                .Select(t => t.Id)
                .Union(technicianAssignmentsByTechId.Keys.Where(id => !leadTechnicianIds.Contains(id)))
                .Distinct()
                .ToList();

            var technicianTargets = allActiveTechs
                .Where(t => technicianTargetIds.Contains(t.Id))
                .Select(t =>
                {
                    assignedTruckNumberByTechId.TryGetValue(t.Id, out var truckNumber);

                    return new DailyAssignmentTargetDto
                    {
                        TargetKey = $"Technician:{t.Id}",
                        TargetType = "Technician",
                        TechnicianId = (int)t.Id,
                        TechnicianName = FormatTechnicianName(t.FirstName, t.LastName, t.EmployeeId),
                        TechnicianTitle = t.Title,
                        Technicians = new List<DailyAssignmentTechnicianDto>
                        {
                MapTechnician(t, rosterDate, null, truckNumber)
                        },
                        AssignedTickets = technicianAssignmentsByTechId.TryGetValue(t.Id, out var assigned)
                            ? assigned
                            : new List<DailyAssignedTicketDto>()
                    };
                })
                .OrderBy(x => x.TechnicianName)
                .ToList();

            var ticketPool = nonClosedTickets
                .Select(t => MapTicketPoolItem(t, assignmentByTicketId, closedStatusNames, fieldCompleteStatusNames))
                .ToList();

            var publishedRows = assignments
                .Where(x => x.IsPublished)
                .OrderByDescending(x => x.PublishedVersion)
                .ThenByDescending(x => x.PublishedAt)
                .ToList();

            var dto = new DailyAssignmentsBoardDto
            {
                WorkDate = rosterDate,

                PublishedVersion = publishedRows.Count == 0 ? 0 : publishedRows.Max(x => x.PublishedVersion),
                LastPublishedAt = publishedRows.FirstOrDefault()?.PublishedAt,
                LastPublishedBy = publishedRows.FirstOrDefault()?.PublishedBy,

                TruckTargets = truckTargets,
                TechnicianTargets = technicianTargets,
                TicketPool = ticketPool
            };

            return Ok(dto);
        }

        [HttpPost("assign")]
        public async Task<ActionResult<AssignDailyTicketsResponse>> AssignTickets([FromBody] AssignDailyTicketsRequest req, CancellationToken ct)
        {
            var rosterDate =
            (req.WorkDate == default
                ? DateTime.Today
                : req.WorkDate).Date;

            var workDate = rosterDate;

            var cleanTargetType = (req.TargetType ?? string.Empty).Trim();

            var assignmentMode = string.IsNullOrWhiteSpace(req.AssignmentMode)
                ? "Move"
                : req.AssignmentMode.Trim();

            var isAddMode =
                assignmentMode.Equals(
                    "Add",
                    StringComparison.OrdinalIgnoreCase);

            var isMoveMode =
                assignmentMode.Equals(
                    "Move",
                    StringComparison.OrdinalIgnoreCase);

            if (!isAddMode && !isMoveMode)
            {
                return BadRequest(
                    "AssignmentMode must be Move or Add.");
            }

            if (!IsTargetType(cleanTargetType, "Truck") && !IsTargetType(cleanTargetType, "Technician"))
                return BadRequest("TargetType must be Truck or Technician.");

            var ticketIds = (req.TicketIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return BadRequest("At least one ticket is required.");

            uint? truckId = null;
            uint? technicianId = null;
            uint? crewId = null;

            if (IsTargetType(cleanTargetType, "Truck"))
            {
                if (!req.TruckId.HasValue || req.TruckId.Value <= 0)
                    return BadRequest("TruckId is required for Truck assignments.");

                truckId = (uint)req.TruckId.Value;

                var truck = await _db.Trucks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == truckId.Value && x.IsActive, ct);

                if (truck == null)
                    return NotFound($"Truck {req.TruckId.Value} was not found or is inactive.");

                crewId = await _db.Crews
                    .AsNoTracking()
                    .Where(c =>
                        c.WorkDate == rosterDate &&
                        c.TruckNumber == truck.TruckNumber)
                    .OrderBy(c => c.Id)
                    .Select(c => (uint?)c.Id)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                if (!req.TechnicianId.HasValue || req.TechnicianId.Value <= 0)
                    return BadRequest("TechnicianId is required for Technician assignments.");

                technicianId = (uint)req.TechnicianId.Value;

                var technicianExists = await ActiveFieldTechniciansQuery()
                    .AnyAsync(x => x.Id == technicianId.Value, ct);

                if (!technicianExists)
                    return NotFound($"Technician {req.TechnicianId.Value} was not found or is inactive.");

                /*
                 * Crew work lists are intentionally owned by their lead technician so that
                 * tickets remain stable when crew membership changes. When TruckId is also
                 * supplied, it identifies the crew context used for display and publishing.
                 */
                if (req.TruckId.HasValue && req.TruckId.Value > 0)
                {
                    truckId = (uint)req.TruckId.Value;

                    var truck = await _db.Trucks
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x => x.Id == truckId.Value && x.IsActive,
                            ct);

                    if (truck == null)
                        return NotFound($"Truck {req.TruckId.Value} was not found or is inactive.");

                    var leadIsAssignedToTruck = await _db.TruckRosters
                        .AsNoTracking()
                        .AnyAsync(
                            x => x.WorkDate == rosterDate &&
                                 x.TruckId == truckId.Value &&
                                 x.TechnicianId == technicianId.Value,
                            ct);

                    if (!leadIsAssignedToTruck)
                    {
                        return BadRequest(
                            "The selected crew lead is no longer assigned to that truck. " +
                            "Refresh Daily Assignments and select the crew again.");
                    }

                    crewId = await _db.Crews
                        .AsNoTracking()
                        .Where(c =>
                            c.WorkDate == rosterDate &&
                            c.TruckNumber == truck.TruckNumber)
                        .OrderBy(c => c.Id)
                        .Select(c => (uint?)c.Id)
                        .FirstOrDefaultAsync(ct);
                }
            }

            if (!req.ConfirmConflictWarnings)
            {
                var conflictWarning = await BuildAssignmentConflictWarningMessageAsync(
                    rosterDate,
                    workDate,
                    cleanTargetType,
                    truckId,
                    technicianId,
                    ct);

                if (!string.IsNullOrWhiteSpace(conflictWarning))
                    return Conflict(conflictWarning);
            }

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            if (tickets.Count != ticketIds.Count)
            {
                var foundIds = tickets.Select(t => t.Id).ToHashSet();
                var missingIds = ticketIds.Where(id => !foundIds.Contains(id)).ToList();

                return NotFound($"One or more tickets were not found: {string.Join(", ", missingIds)}");
            }

            var closedStatusNames = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive && x.IsClosed)
                .Select(x => x.Name)
                .ToListAsync(ct);

            var closedStatusSet = closedStatusNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var closedTickets = tickets
                .Where(t => closedStatusSet.Contains(t.Status ?? string.Empty))
                .Select(t => t.Id)
                .ToList();

            if (closedTickets.Count > 0)
                return BadRequest($"Closed tickets cannot be assigned: {string.Join(", ", closedTickets)}");

            var existingAssignments = await _db.DailyTicketAssignments
    .Where(x =>
        x.AssignmentDate == workDate &&
        x.AssignmentStatus == AssignmentStatusActive &&
        ticketIds.Contains(x.TicketId))
    .ToListAsync(ct);

            /*
             * A ticket may now have more than one active Daily Assignment.
             *
             * Do NOT collapse these rows to one assignment per TicketId.
             */
            var existingAssignmentsByTicketId =
                existingAssignments
                    .GroupBy(x => x.TicketId)
                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .OrderBy(x => x.SortOrder)
                            .ThenBy(x => x.Id)
                            .ToList());

            bool IsSameTarget(DailyTicketAssignmentEntity assignment)
            {
                if (!string.Equals(
                        assignment.TargetType,
                        cleanTargetType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                /*
                 * Current crew routes are technician-owned.
                 * TruckId is display/crew context and must not create a second
                 * identity for the same technician-owned route.
                 */
                if (IsTargetType(cleanTargetType, "Technician"))
                {
                    return
                        assignment.TechnicianId ==
                        technicianId;
                }

                return
                    assignment.TruckId ==
                    truckId;
            }

            var currentMaxSortOrder = await _db.DailyTicketAssignments
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == workDate &&
                    x.AssignmentStatus == AssignmentStatusActive &&
                    x.TargetType == cleanTargetType &&
                    x.TruckId == truckId &&
                    x.TechnicianId == technicianId)
                .Select(x => (int?)x.SortOrder)
                .MaxAsync(ct) ?? 0;

            var now = DateTime.Now;
            var updatedBy = string.IsNullOrWhiteSpace(req.UpdatedBy)
                ? "Dispatcher"
                : req.UpdatedBy.Trim();

            var assignmentNotes = string.IsNullOrWhiteSpace(req.AssignmentNotes)
                ? null
                : req.AssignmentNotes.Trim();

            var assignmentIds = new List<ulong>();

            foreach (var ticketId in ticketIds)
            {
                currentMaxSortOrder += 10;

                existingAssignmentsByTicketId.TryGetValue(
                    ticketId,
                    out var ticketAssignments);

                ticketAssignments ??=
                    new List<DailyTicketAssignmentEntity>();

                var assignmentForThisTarget =
                    ticketAssignments
                        .FirstOrDefault(IsSameTarget);

                /*
                 * ------------------------------------------------------------
                 * ADD MODE
                 * ------------------------------------------------------------
                 *
                 * Keep every existing crew assignment.
                 *
                 * If this exact target already owns the ticket, simply refresh that
                 * assignment rather than creating a duplicate row.
                 */
                if (isAddMode)
                {
                    if (assignmentForThisTarget != null)
                    {
                        assignmentForThisTarget.TruckId =
                            truckId;

                        assignmentForThisTarget.CrewId =
                            crewId;

                        if (assignmentNotes != null)
                        {
                            assignmentForThisTarget.AssignmentNotes =
                                assignmentNotes;
                        }

                        assignmentForThisTarget.IsPublished =
                            false;

                        assignmentForThisTarget.UpdatedAt =
                            now;

                        assignmentForThisTarget.UpdatedBy =
                            updatedBy;

                        assignmentIds.Add(
                            assignmentForThisTarget.Id);

                        continue;
                    }

                    var additionalAssignment =
                        new DailyTicketAssignmentEntity
                        {
                            AssignmentDate =
                                workDate,

                            TicketId =
                                ticketId,

                            TargetType =
                                cleanTargetType,

                            TruckId =
                                truckId,

                            TechnicianId =
                                technicianId,

                            CrewId =
                                crewId,

                            SortOrder =
                                currentMaxSortOrder,

                            IsPublished =
                                false,

                            PublishedVersion =
                                0,

                            PublishedAt =
                                null,

                            PublishedBy =
                                null,

                            AssignmentNotes =
                                assignmentNotes,

                            AssignmentStatus =
                                AssignmentStatusActive,

                            CreatedAt =
                                now,

                            CreatedBy =
                                updatedBy,

                            UpdatedAt =
                                now,

                            UpdatedBy =
                                updatedBy
                        };

                    _db.DailyTicketAssignments.Add(
                        additionalAssignment);

                    await _db.SaveChangesAsync(ct);

                    assignmentIds.Add(
                        additionalAssignment.Id);

                    continue;
                }

                /*
                 * ------------------------------------------------------------
                 * MOVE MODE
                 * ------------------------------------------------------------
                 *
                 * Preserve today's normal behavior:
                 * after the move, only the destination target remains active.
                 *
                 * This also makes Move deterministic once multi-crew assignments
                 * begin to exist.
                 */
                if (assignmentForThisTarget != null)
                {
                    assignmentForThisTarget.TruckId =
                        truckId;

                    assignmentForThisTarget.TechnicianId =
                        technicianId;

                    assignmentForThisTarget.CrewId =
                        crewId;

                    assignmentForThisTarget.SortOrder =
                        currentMaxSortOrder;

                    if (assignmentNotes != null)
                    {
                        assignmentForThisTarget.AssignmentNotes =
                            assignmentNotes;
                    }

                    assignmentForThisTarget.IsPublished =
                        false;

                    assignmentForThisTarget.UpdatedAt =
                        now;

                    assignmentForThisTarget.UpdatedBy =
                        updatedBy;

                    /*
                     * Any other active crew copies are withdrawn because Dispatch
                     * explicitly chose Move rather than Add.
                     */
                    foreach (var otherAssignment in
                             ticketAssignments.Where(
                                 x => x.Id != assignmentForThisTarget.Id))
                    {
                        otherAssignment.AssignmentStatus =
                            AssignmentStatusRemoved;

                        otherAssignment.RemovedAt =
                            now;

                        otherAssignment.RemovedBy =
                            updatedBy;

                        otherAssignment.IsPublished =
                            false;

                        otherAssignment.UpdatedAt =
                            now;

                        otherAssignment.UpdatedBy =
                            updatedBy;
                    }

                    assignmentIds.Add(
                        assignmentForThisTarget.Id);

                    continue;
                }

                /*
                 * No assignment already exists for the destination.
                 *
                 * If the ticket was assigned elsewhere, reuse the newest existing
                 * assignment as the moved row and withdraw any additional rows.
                 */
                var assignmentToMove =
                    ticketAssignments
                        .OrderByDescending(x => x.UpdatedAt)
                        .ThenByDescending(x => x.Id)
                        .FirstOrDefault();

                if (assignmentToMove != null)
                {
                    assignmentToMove.TargetType =
                        cleanTargetType;

                    assignmentToMove.TruckId =
                        truckId;

                    assignmentToMove.TechnicianId =
                        technicianId;

                    assignmentToMove.CrewId =
                        crewId;

                    assignmentToMove.SortOrder =
                        currentMaxSortOrder;

                    if (assignmentNotes != null)
                    {
                        assignmentToMove.AssignmentNotes =
                            assignmentNotes;
                    }

                    assignmentToMove.IsPublished =
                        false;

                    assignmentToMove.UpdatedAt =
                        now;

                    assignmentToMove.UpdatedBy =
                        updatedBy;

                    foreach (var otherAssignment in
                             ticketAssignments.Where(
                                 x => x.Id != assignmentToMove.Id))
                    {
                        otherAssignment.AssignmentStatus =
                            AssignmentStatusRemoved;

                        otherAssignment.RemovedAt =
                            now;

                        otherAssignment.RemovedBy =
                            updatedBy;

                        otherAssignment.IsPublished =
                            false;

                        otherAssignment.UpdatedAt =
                            now;

                        otherAssignment.UpdatedBy =
                            updatedBy;
                    }

                    assignmentIds.Add(
                        assignmentToMove.Id);

                    continue;
                }

                /*
                 * Brand-new ticket assignment.
                 */
                var newAssignment =
                    new DailyTicketAssignmentEntity
                    {
                        AssignmentDate =
                            workDate,

                        TicketId =
                            ticketId,

                        TargetType =
                            cleanTargetType,

                        TruckId =
                            truckId,

                        TechnicianId =
                            technicianId,

                        CrewId =
                            crewId,

                        SortOrder =
                            currentMaxSortOrder,

                        IsPublished =
                            false,

                        PublishedVersion =
                            0,

                        PublishedAt =
                            null,

                        PublishedBy =
                            null,

                        AssignmentNotes =
                            assignmentNotes,

                        AssignmentStatus =
                            AssignmentStatusActive,

                        CreatedAt =
                            now,

                        CreatedBy =
                            updatedBy,

                        UpdatedAt =
                            now,

                        UpdatedBy =
                            updatedBy
                    };

                _db.DailyTicketAssignments.Add(
                    newAssignment);

                await _db.SaveChangesAsync(ct);

                assignmentIds.Add(
                    newAssignment.Id);
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new AssignDailyTicketsResponse
            {
                WorkDate = workDate,
                AssignedCount = assignmentIds.Count,
                AssignmentIds = assignmentIds
            });
        }

        [HttpPost("remove")]
        public async Task<ActionResult<RemoveDailyTicketAssignmentsResponse>>RemoveAssignments(
            [FromBody] RemoveDailyTicketAssignmentsRequest req,
            CancellationToken ct)
        {
            var workDate =
                (req.WorkDate == default
                    ? DateTime.Today
                    : req.WorkDate).Date;

            var assignmentIds =
                (req.AssignmentIds ?? new List<ulong>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

            var ticketIds =
                (req.TicketIds ?? new List<long>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

            if (assignmentIds.Count == 0 &&
                ticketIds.Count == 0)
            {
                return BadRequest(
                    "At least one assignment or ticket is required.");
            }

            /*
             * AssignmentId is now the preferred identity.
             *
             * This matters because one ticket may legitimately have multiple
             * active Daily Assignment rows owned by different crews.
             *
             * TicketIds remain only as a backward-compatible fallback until
             * all callers have been migrated.
             */
            var assignmentsQuery =
                _db.DailyTicketAssignments
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.AssignmentStatus ==
                            AssignmentStatusActive);

            if (assignmentIds.Count > 0)
            {
                assignmentsQuery =
                    assignmentsQuery.Where(
                        x => assignmentIds.Contains(x.Id));
            }
            else
            {
                assignmentsQuery =
                    assignmentsQuery.Where(
                        x => ticketIds.Contains(x.TicketId));
            }

            var assignments =
                await assignmentsQuery
                    .ToListAsync(ct);

            if (assignments.Count == 0)
            {
                return Ok(
                    new RemoveDailyTicketAssignmentsResponse
                    {
                        WorkDate = workDate,
                        RemovedCount = 0,
                        RemovedTicketIds =
                            new List<long>()
                    });
            }

            var removedTicketIds =
                assignments
                    .Select(x => x.TicketId)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

            var now = DateTime.Now;

            var removedBy =
                string.IsNullOrWhiteSpace(req.UpdatedBy)
                    ? "Dispatcher"
                    : req.UpdatedBy.Trim();

            foreach (var assignment in assignments)
            {
                assignment.AssignmentStatus =
                    AssignmentStatusRemoved;

                assignment.RemovedAt =
                    now;

                assignment.RemovedBy =
                    removedBy;

                assignment.IsPublished =
                    false;

                assignment.UpdatedAt =
                    now;

                assignment.UpdatedBy =
                    removedBy;
            }

            /*
             * Removing a ticket from Daily Assignments must also keep the
             * ticket's assignment display in sync.
             *
             * Only clear AssignedTech / AssignedCrewId when the ticket has
             * no other active Daily Assignment rows remaining.
             *
             * Ticket Status is intentionally left unchanged.
             */
            var removedAssignmentIds =
                assignments
                    .Select(x => x.Id)
                    .ToList();

            var stillAssignedTicketIds =
                await _db.DailyTicketAssignments
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.AssignmentStatus == AssignmentStatusActive &&
                        removedTicketIds.Contains(x.TicketId) &&
                        !removedAssignmentIds.Contains(x.Id))
                    .Select(x => x.TicketId)
                    .Distinct()
                    .ToListAsync(ct);

            var nowUnassignedTicketIds =
                removedTicketIds
                    .Except(stillAssignedTicketIds)
                    .ToList();

            if (nowUnassignedTicketIds.Count > 0)
            {
                var ticketsNowUnassigned =
                    await _db.Tickets
                        .Where(x =>
                            nowUnassignedTicketIds.Contains(x.Id))
                        .ToListAsync(ct);

                foreach (var ticket in ticketsNowUnassigned)
                {
                    ticket.AssignedTech =
                        "(Unassigned)";

                    ticket.AssignedCrewId =
                        null;

                    ticket.LastActivityAt =
                        now;
                }
            }

            await _db.SaveChangesAsync(ct);

            return Ok(
                new RemoveDailyTicketAssignmentsResponse
                {
                    WorkDate = workDate,

                    /*
                     * Count actual assignment rows removed rather than unique
                     * ticket IDs, since multi-crew tickets may have more than
                     * one assignment.
                     */
                    RemovedCount =
                        assignments.Count,

                    RemovedTicketIds =
                        removedTicketIds
                });
        }

        [HttpPost("migrate-truck-targets-to-lead-techs")]
        public async Task<IActionResult> MigrateTruckTargetAssignmentsToLeadTechs([FromQuery] string? date = null, CancellationToken ct = default)
        {
            var rosterDate = ParseDateOrToday(date);
            var assignmentDate = rosterDate;
            var now = DateTime.Now;

            var truckAssignments = await _db.DailyTicketAssignments
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.AssignmentStatus == AssignmentStatusActive &&
                    x.TargetType == "Truck" &&
                    x.TruckId.HasValue)
                .OrderBy(x => x.TruckId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            if (truckAssignments.Count == 0)
                return Ok("No truck-owned assignments were found to migrate.");

            var trucks = await _db.Trucks
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(ct);

            var truckById = trucks.ToDictionary(x => x.Id);

            var truckNumbers = trucks
                .Select(x => x.TruckNumber)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var crews = await _db.Crews
                .AsNoTracking()
                .Where(c =>
                    c.WorkDate == rosterDate &&
                    c.TruckNumber != null &&
                    truckNumbers.Contains(c.TruckNumber))
                .ToListAsync(ct);

            var crewByTruckNumber = crews
                .Select(c => new
                {
                    Crew = c,
                    TruckNumber = (c.TruckNumber ?? string.Empty).Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber))
                .GroupBy(x => x.TruckNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Crew).OrderBy(x => x.Id).First(),
                    StringComparer.OrdinalIgnoreCase);

            var rosterRows = await (
                from roster in _db.TruckRosters.AsNoTracking()
                join tech in ActiveFieldTechniciansQuery()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == rosterDate
                select new
                {
                    roster.TruckId,
                    Technician = tech
                })
                .ToListAsync(ct);

            var rosterTechsByTruckId = rosterRows
                .GroupBy(x => x.TruckId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Technician)
                          .OrderByDescending(GetTitleRank)
                          .ThenBy(x => x.LastName)
                          .ThenBy(x => x.FirstName)
                          .ToList());

            var migrated = 0;
            var skipped = 0;

            foreach (var assignment in truckAssignments)
            {
                if (!assignment.TruckId.HasValue ||
                    !truckById.TryGetValue(assignment.TruckId.Value, out var truck))
                {
                    skipped++;
                    continue;
                }

                var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

                CrewEntity? crew = null;

                if (!string.IsNullOrWhiteSpace(truckNumber))
                    crewByTruckNumber.TryGetValue(truckNumber, out crew);

                var rosterTechs = rosterTechsByTruckId.TryGetValue(truck.Id, out var techs)
                    ? techs
                    : new List<TechnicianEntity>();

                var leadTech = PickLeadTechnician(
                    rosterTechs,
                    truck.Id,
                    crew?.LeadTechnicianId);

                if (leadTech == null)
                {
                    skipped++;
                    continue;
                }

                assignment.TargetType = "Technician";
                assignment.TechnicianId = leadTech.Id;

                // TruckId is no longer part of the assignment identity.
                // The truck comes from today's roster.
                assignment.TruckId = null;

                assignment.CrewId = crew?.Id;
                assignment.IsPublished = false;
                assignment.UpdatedAt = now;
                assignment.UpdatedBy = "Truck-to-lead migration";

                migrated++;
            }

            await _db.SaveChangesAsync(ct);

            return Ok($"Migrated {migrated} assignment(s). Skipped {skipped} assignment(s).");
        }

        [HttpPost("reorder")]
        public async Task<ActionResult<ReorderDailyTicketAssignmentsResponse>> ReorderAssignments([FromBody] ReorderDailyTicketAssignmentsRequest req, 
            CancellationToken ct)
        {
            var workDate =
                (req.WorkDate == default
                    ? DateTime.Today
                    : req.WorkDate).Date;

            var cleanTargetType = (req.TargetType ?? string.Empty).Trim();

            if (!IsTargetType(cleanTargetType, "Truck") && !IsTargetType(cleanTargetType, "Technician"))
                return BadRequest("TargetType must be Truck or Technician.");

            var orderedTicketIds = (req.TicketIdsInOrder ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (orderedTicketIds.Count == 0)
                return BadRequest("TicketIdsInOrder is required.");

            uint? truckId = null;
            uint? technicianId = null;

            if (IsTargetType(cleanTargetType, "Truck"))
            {
                if (!req.TruckId.HasValue || req.TruckId.Value <= 0)
                    return BadRequest("TruckId is required for Truck reorder.");

                truckId = (uint)req.TruckId.Value;

                var truckExists = await _db.Trucks
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == truckId.Value && x.IsActive, ct);

                if (!truckExists)
                    return NotFound($"Truck {req.TruckId.Value} was not found or is inactive.");
            }
            else
            {
                if (!req.TechnicianId.HasValue || req.TechnicianId.Value <= 0)
                    return BadRequest("TechnicianId is required for Technician reorder.");

                technicianId = (uint)req.TechnicianId.Value;

                var technicianExists = await ActiveFieldTechniciansQuery()
                    .AnyAsync(x => x.Id == technicianId.Value, ct);

                if (!technicianExists)
                    return NotFound($"Technician {req.TechnicianId.Value} was not found or is inactive.");

                // A Technician target with TruckId is a crew work list anchored
                // to its lead technician. Preserve that context when reordering.
                if (req.TruckId.HasValue && req.TruckId.Value > 0)
                {
                    truckId = (uint)req.TruckId.Value;

                    var truckExists = await _db.Trucks
                        .AsNoTracking()
                        .AnyAsync(x => x.Id == truckId.Value && x.IsActive, ct);

                    if (!truckExists)
                        return NotFound($"Truck {req.TruckId.Value} was not found or is inactive.");
                }
            }

            /*
             * Technician-owned routes are identified by the lead TechnicianId.
             * TruckId is current display/crew context and must not split one lead
             * technician's route into multiple assignment identities.
             */
            var targetAssignmentsQuery = _db.DailyTicketAssignments
                .Where(x =>
                    x.AssignmentDate == workDate &&
                    x.AssignmentStatus == AssignmentStatusActive &&
                    x.TargetType == cleanTargetType);

            if (cleanTargetType == "Technician")
            {
                targetAssignmentsQuery = targetAssignmentsQuery
                    .Where(x => x.TechnicianId == technicianId);
            }
            else
            {
                targetAssignmentsQuery = targetAssignmentsQuery
                    .Where(x => x.TruckId == truckId);
            }

            var assignments = await targetAssignmentsQuery
                .ToListAsync(ct);

            if (assignments.Count == 0)
                return NotFound("No assignments were found for this target.");

            var assignmentByTicketId = assignments
                .GroupBy(x => x.TicketId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());

            var missingTicketIds = orderedTicketIds
                .Where(id => !assignmentByTicketId.ContainsKey(id))
                .ToList();

            if (missingTicketIds.Count > 0)
            {
                return BadRequest(
                    "One or more tickets are not assigned to this target: " +
                    string.Join(", ", missingTicketIds));
            }

            var now = DateTime.Now;

            var updatedBy = string.IsNullOrWhiteSpace(req.UpdatedBy)
                ? "Dispatcher"
                : req.UpdatedBy.Trim();

            var sortOrder = 0;

            foreach (var ticketId in orderedTicketIds)
            {
                sortOrder += 10;

                var assignment = assignmentByTicketId[ticketId];

                assignment.SortOrder = sortOrder;

                /*
                 * Keep the current truck only as crew display context. The technician
                 * remains the stable owner of the route.
                 */
                if (cleanTargetType == "Technician")
                    assignment.TruckId = truckId;

                assignment.IsPublished = false;
                assignment.UpdatedAt = now;
                assignment.UpdatedBy = updatedBy;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new ReorderDailyTicketAssignmentsResponse
            {
                WorkDate = workDate,
                TargetType = cleanTargetType,
                TruckId = truckId == null ? null : (int?)truckId.Value,
                TechnicianId = technicianId == null ? null : (int?)technicianId.Value,
                ReorderedCount = orderedTicketIds.Count
            });
        }

        [HttpPost("carryover")]
        public async Task<ActionResult<CarryOverDailyAssignmentsResponse>> CarryOverAssignments([FromBody] CarryOverDailyAssignmentsRequest req,
            CancellationToken ct)
        {
            var workDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;

            var createdBy = string.IsNullOrWhiteSpace(req.CreatedBy)
                ? "Dispatcher"
                : req.CreatedBy.Trim();

            DateTime? sourceDate = req.FromDate?.Date;

            if (sourceDate == null)
            {
                sourceDate = await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x => x.AssignmentDate < workDate)
                    .OrderByDescending(x => x.AssignmentDate)
                    .Select(x => (DateTime?)x.AssignmentDate)
                    .FirstOrDefaultAsync(ct);
            }

            if (sourceDate == null)
            {
                return Ok(new CarryOverDailyAssignmentsResponse
                {
                    WorkDate = workDate,
                    SourceDate = null,
                    SourcePublishedVersion = 0,
                    Message = "No previous published assignment board was found."
                });
            }

            var sourcePublishedVersion = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x => x.AssignmentDate == sourceDate.Value)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct) ?? 0;

            if (sourcePublishedVersion == 0)
            {
                return Ok(new CarryOverDailyAssignmentsResponse
                {
                    WorkDate = workDate,
                    SourceDate = sourceDate,
                    SourcePublishedVersion = 0,
                    Message = "The selected source date has no published assignments."
                });
            }

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

            var closedStatusNames = statusRows
                .Where(x => x.IsClosed)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fieldCompleteStatusNames = statusRows
                .Where(x => x.IsFieldComplete)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sourceRows = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Include(x => x.Ticket)
                .Where(x =>
                    x.AssignmentDate == sourceDate.Value &&
                    x.PublishedVersion == sourcePublishedVersion)
                .OrderBy(x => x.TargetType)
                .ThenBy(x => x.TruckId)
                .ThenBy(x => x.TechnicianId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            if (sourceRows.Count == 0)
            {
                return Ok(new CarryOverDailyAssignmentsResponse
                {
                    WorkDate = workDate,
                    SourceDate = sourceDate,
                    SourcePublishedVersion = sourcePublishedVersion,
                    Message = "No assignments were found on the selected source board."
                });
            }

            var existingTodayTicketIds = await _db.DailyTicketAssignments
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == workDate &&
                    x.AssignmentStatus == AssignmentStatusActive)
                .Select(x => x.TicketId)
                .ToListAsync(ct);

            var existingTodayTicketIdSet = existingTodayTicketIds.ToHashSet();

            var activeTrucks = await _db.Trucks
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new
                {
                    x.Id,
                    x.TruckNumber
                })
                .ToListAsync(ct);

            var activeTruckIds = activeTrucks
                .Select(x => x.Id)
                .ToHashSet();

            var truckNumberById = activeTrucks
                .ToDictionary(x => x.Id, x => x.TruckNumber ?? "");

            var activeTechnicianIds = await _db.Technicians
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var activeTechnicianIdSet = activeTechnicianIds.ToHashSet();

            var todayCrews = await _db.Crews
                .AsNoTracking()
                .Where(x => x.WorkDate == workDate)
                .ToListAsync(ct);

            var todayCrewByTruckNumber = todayCrews
                .Select(c => new
                {
                    Crew = c,
                    TruckNumber = (c.TruckNumber ?? string.Empty).Trim()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber))
                .GroupBy(x => x.TruckNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Crew).OrderBy(x => x.Id).First(),
                    StringComparer.OrdinalIgnoreCase);

            var skippedAlreadyAssigned = 0;
            var skippedCompletedOrClosed = 0;
            var skippedInvalidTarget = 0;

            var carryCandidates = new List<DailyTicketAssignmentPublishedEntity>();

            foreach (var row in sourceRows)
            {
                if (existingTodayTicketIdSet.Contains(row.TicketId))
                {
                    skippedAlreadyAssigned++;
                    continue;
                }

                var status = row.Ticket?.Status ?? "";

                if (closedStatusNames.Contains(status) || fieldCompleteStatusNames.Contains(status))
                {
                    skippedCompletedOrClosed++;
                    continue;
                }

                var targetType = NormalizeTargetType(row.TargetType);

                if (targetType == "Truck")
                {
                    if (!row.TruckId.HasValue || !activeTruckIds.Contains(row.TruckId.Value))
                    {
                        skippedInvalidTarget++;
                        continue;
                    }
                }
                else if (targetType == "Technician")
                {
                    if (!row.TechnicianId.HasValue || !activeTechnicianIdSet.Contains(row.TechnicianId.Value))
                    {
                        skippedInvalidTarget++;
                        continue;
                    }
                }
                else
                {
                    skippedInvalidTarget++;
                    continue;
                }

                carryCandidates.Add(row);
                existingTodayTicketIdSet.Add(row.TicketId);
            }

            if (carryCandidates.Count == 0)
            {
                return Ok(new CarryOverDailyAssignmentsResponse
                {
                    WorkDate = workDate,
                    SourceDate = sourceDate,
                    SourcePublishedVersion = sourcePublishedVersion,
                    SkippedAlreadyAssignedCount = skippedAlreadyAssigned,
                    SkippedCompletedOrClosedCount = skippedCompletedOrClosed,
                    SkippedInvalidTargetCount = skippedInvalidTarget,
                    Message = "No tickets needed to be carried over."
                });
            }

            var now = DateTime.Now;
            var newRows = new List<DailyTicketAssignmentEntity>();

            var groups = carryCandidates
                .GroupBy(x => new
                {
                    TargetType = NormalizeTargetType(x.TargetType),
                    x.TruckId,
                    x.TechnicianId
                })
                .ToList();

            foreach (var group in groups)
            {
                var targetType = group.Key.TargetType;
                var truckId = group.Key.TruckId;
                var technicianId = group.Key.TechnicianId;

                var groupItems = group
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();

                var shiftAmount = groupItems.Count * 10;

                var existingTargetRows = await _db.DailyTicketAssignments
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.AssignmentStatus == AssignmentStatusActive &&
                        x.TargetType == targetType &&
                        x.TruckId == truckId &&
                        x.TechnicianId == technicianId)
                    .ToListAsync(ct);

                foreach (var existing in existingTargetRows)
                {
                    existing.SortOrder += shiftAmount;
                    existing.IsPublished = false;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = createdBy;
                }

                var sortOrder = 0;

                foreach (var sourceRow in groupItems)
                {
                    sortOrder += 10;

                    uint? crewId = null;

                    if (targetType == "Truck" && sourceRow.TruckId.HasValue)
                    {
                        if (truckNumberById.TryGetValue(sourceRow.TruckId.Value, out var truckNumber) &&
                            !string.IsNullOrWhiteSpace(truckNumber) &&
                            todayCrewByTruckNumber.TryGetValue(truckNumber.Trim(), out var todayCrew))
                        {
                            crewId = todayCrew.Id;
                        }
                    }

                    newRows.Add(new DailyTicketAssignmentEntity
                    {
                        AssignmentDate = workDate,
                        TicketId = sourceRow.TicketId,

                        TargetType = targetType,
                        TruckId = targetType == "Truck" ? sourceRow.TruckId : null,
                        TechnicianId = targetType == "Technician" ? sourceRow.TechnicianId : null,
                        CrewId = crewId,

                        SortOrder = sortOrder,

                        IsPublished = false,
                        PublishedVersion = 0,
                        PublishedAt = null,
                        PublishedBy = null,

                        CarriedFromAssignmentId = sourceRow.SourceAssignmentId,

                        AssignmentNotes = sourceRow.AssignmentNotes,

                        CreatedAt = now,
                        CreatedBy = createdBy,
                        UpdatedAt = now,
                        UpdatedBy = createdBy
                    });
                }
            }

            _db.DailyTicketAssignments.AddRange(newRows);

            await _db.SaveChangesAsync(ct);

            return Ok(new CarryOverDailyAssignmentsResponse
            {
                WorkDate = workDate,
                SourceDate = sourceDate,
                SourcePublishedVersion = sourcePublishedVersion,

                CarriedOverCount = newRows.Count,
                SkippedAlreadyAssignedCount = skippedAlreadyAssigned,
                SkippedCompletedOrClosedCount = skippedCompletedOrClosed,
                SkippedInvalidTargetCount = skippedInvalidTarget,

                CarriedOverTicketIds = newRows
                    .Select(x => x.TicketId)
                    .OrderBy(x => x)
                    .ToList(),

                NewAssignmentIds = newRows
                    .Select(x => x.Id)
                    .OrderBy(x => x)
                    .ToList(),

                Message = $"Carried over {newRows.Count} ticket(s)."
            });
        }

        [HttpPost("publish-target")]
        public async Task<ActionResult<PublishDailyAssignmentTargetResponse>> PublishTargetAssignments([FromBody] PublishDailyAssignmentTargetRequest req,
            CancellationToken ct)
        {
            var rosterDate =
                (req.WorkDate == default
                    ? DateTime.Today
                    : req.WorkDate).Date;

            var workDate = rosterDate;

            var cleanTargetType = NormalizeTargetType(req.TargetType);

            if (cleanTargetType != "Truck" && cleanTargetType != "Technician")
                return BadRequest("TargetType must be Truck or Technician.");

            uint? truckId = null;
            uint? technicianId = null;

            if (cleanTargetType == "Truck")
            {
                if (!req.TruckId.HasValue || req.TruckId.Value <= 0)
                    return BadRequest("TruckId is required for Truck publish.");

                truckId = (uint)req.TruckId.Value;

                var truckExists = await _db.Trucks
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == truckId.Value && x.IsActive, ct);

                if (!truckExists)
                    return NotFound($"Truck {req.TruckId.Value} was not found or is inactive.");
            }
            else
            {
                if (!req.TechnicianId.HasValue || req.TechnicianId.Value <= 0)
                    return BadRequest("TechnicianId is required for Technician publish.");

                technicianId = (uint)req.TechnicianId.Value;

                var techExists = await ActiveFieldTechniciansQuery()
                    .AnyAsync(x => x.Id == technicianId.Value, ct);

                if (!techExists)
                    return NotFound($"Technician {req.TechnicianId.Value} was not found or is inactive.");

                // Technician targets with TruckId are crew lists anchored to their lead tech.
                if (req.TruckId.HasValue && req.TruckId.Value > 0)
                {
                    truckId = (uint)req.TruckId.Value;

                    var truckExists = await _db.Trucks
                        .AsNoTracking()
                        .AnyAsync(x => x.Id == truckId.Value && x.IsActive, ct);

                    if (!truckExists)
                        return NotFound($"Truck {req.TruckId.Value} was not found or is inactive.");
                }
            }

            var publishedBy = string.IsNullOrWhiteSpace(req.PublishedBy)
                ? "Dispatcher"
                : req.PublishedBy.Trim();

            var now = DateTime.Now;

            /*
             * Resolve configurable workflow statuses before loading the publishable draft.
             * Closed tickets may still have old draft assignment rows, but they must not be
             * republished into the field technician route.
             */
            var statusRows = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new
                {
                    x.Name,
                    x.IsClosed,
                    x.IsFieldComplete,
                    x.IsAssignmentPublishTarget,
                    x.IsUnassignmentTarget
                })
                .ToListAsync(ct);

            var protectedStatusNames = statusRows
                .Where(x => x.IsClosed || x.IsFieldComplete)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var closedStatusNamesForPublish = statusRows
                .Where(x => x.IsClosed)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            /*
             * Technician route ownership is by TechnicianId only.
             * TruckId is current crew/display context and must not split the route.
             */
            var rawDraftAssignments = await FilterDraftTargetRows(
                    _db.DailyTicketAssignments.Include(x => x.Ticket),
                    workDate,
                    cleanTargetType,
                    truckId,
                    technicianId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            /*
             * Closed tickets are hidden from Dispatcher Daily Assignments and must not be
             * re-published from stale draft rows.
             */
            var draftAssignments = rawDraftAssignments
                .Where(x => x.Ticket != null)
                .Where(x => !closedStatusNamesForPublish.Contains(x.Ticket!.Status ?? string.Empty))
                .ToList();

            var currentTicketIds = draftAssignments
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            /*
             * Look up the previous active route snapshot for this target using the same
             * route identity rule: Technician targets are owned by TechnicianId only.
             */
            var previousTargetPublishedVersion = await FilterPublishedTargetRows(
                    _db.DailyTicketAssignmentPublished.AsNoTracking(),
                    workDate,
                    cleanTargetType,
                    truckId,
                    technicianId)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct);

            var previousPublishedRowsForEmail = previousTargetPublishedVersion.HasValue
                ? await FilterPublishedTargetRows(
                        _db.DailyTicketAssignmentPublished.AsNoTracking(),
                        workDate,
                        cleanTargetType,
                        truckId,
                        technicianId)
                    .Where(x => x.PublishedVersion == previousTargetPublishedVersion.Value)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToListAsync(ct)
                : new List<DailyTicketAssignmentPublishedEntity>();

            var previouslyPublishedTicketIds = previousPublishedRowsForEmail
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            var isModifiedPublish = previousPublishedRowsForEmail.Count > 0;

            var releasedTicketIds = previouslyPublishedTicketIds
                .Except(currentTicketIds)
                .Distinct()
                .ToList();

            var nextPublishedVersion = (await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x => x.AssignmentDate == workDate)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct) ?? 0) + 1;

            var affectedTicketIds = currentTicketIds
                .Concat(releasedTicketIds)
                .Distinct()
                .ToList();

            var affectedTickets = affectedTicketIds.Count == 0
                ? new List<TicketEntity>()
                : await _db.Tickets
                    .Where(x => affectedTicketIds.Contains(x.Id))
                    .ToListAsync(ct);

            var ticketsById = affectedTickets.ToDictionary(x => x.Id);

            var currentTicketsById = ticketsById
                .Where(x => currentTicketIds.Contains(x.Key))
                .ToDictionary(x => x.Key, x => x.Value);

            var missingCurrentTicketIds = currentTicketIds
                .Where(id => !ticketsById.ContainsKey(id))
                .ToList();

            if (missingCurrentTicketIds.Count > 0)
            {
                return NotFound(
                    $"One or more tickets were not found: {string.Join(", ", missingCurrentTicketIds)}");
            }

            /*
             * A released ticket should not be returned to unassigned if it has already been
             * published somewhere else.
             */
            var publishedElsewhereTicketIds = new HashSet<long>();

            if (releasedTicketIds.Count > 0)
            {
                var publishedElsewhereQuery = _db.DailyTicketAssignments
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.IsPublished &&
                        releasedTicketIds.Contains(x.TicketId));

                if (cleanTargetType.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                {
                    publishedElsewhereQuery = publishedElsewhereQuery
                        .Where(x =>
                            !(x.TargetType == "Technician" &&
                              x.TechnicianId == technicianId));
                }
                else
                {
                    publishedElsewhereQuery = publishedElsewhereQuery
                        .Where(x =>
                            !(x.TargetType == "Truck" &&
                              x.TruckId == truckId));
                }


                publishedElsewhereTicketIds = (await publishedElsewhereQuery
                        .Select(x => x.TicketId)
                        .Distinct()
                        .ToListAsync(ct))
                    .ToHashSet();
            }

            var ticketsToUnassign = releasedTicketIds
                .Where(id => ticketsById.ContainsKey(id))
                .Where(id => !publishedElsewhereTicketIds.Contains(id))
                .Where(id => !protectedStatusNames.Contains(ticketsById[id].Status ?? ""))
                .ToList();

            /*
             * Load display context for published assignments.
             * Technician targets with TruckId use crew names for Assigned To.
             */
            var truckIds = draftAssignments
                .Where(x => x.TruckId.HasValue)
                .Select(x => x.TruckId!.Value)
                .Distinct()
                .ToList();

            var technicianIds = draftAssignments
                .Where(x => x.TechnicianId.HasValue)
                .Select(x => x.TechnicianId!.Value)
                .Distinct()
                .ToList();

            var trucks = await _db.Trucks
                .AsNoTracking()
                .Where(x => truckIds.Contains(x.Id))
                .ToListAsync(ct);

            var trucksById = trucks.ToDictionary(x => x.Id);

            var directTechnicians = await _db.Technicians
                .AsNoTracking()
                .Where(x => technicianIds.Contains(x.Id))
                .ToListAsync(ct);

            var techniciansById = directTechnicians.ToDictionary(x => x.Id);

            var technicianEmailRecipientsById = directTechnicians
                .ToDictionary(
                    x => x.Id,
                    x => new DailyAssignmentEmailRecipient
                    {
                        Name = FormatTechnicianName(
                            x.FirstName,
                            x.LastName,
                            x.EmployeeId),
                        EmailAddress = (x.EmailAddress ?? string.Empty).Trim()
                    });

            var truckRosterRows = await (
                from roster in _db.TruckRosters.AsNoTracking()
                join tech in _db.Technicians.AsNoTracking()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == rosterDate
                      && truckIds.Contains(roster.TruckId)
                      && tech.IsActive
                select new
                {
                    roster.TruckId,
                    tech.EmployeeId,
                    tech.FirstName,
                    tech.LastName,
                    tech.EmailAddress
                })
                .ToListAsync(ct);

            var truckTechNamesByTruckId = truckRosterRows
                .GroupBy(x => x.TruckId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => FormatTechnicianName(
                            x.FirstName,
                            x.LastName,
                            x.EmployeeId))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList());

            var truckEmailRecipientsByTruckId = truckRosterRows
                .GroupBy(x => x.TruckId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new DailyAssignmentEmailRecipient
                    {
                        Name = FormatTechnicianName(
                            x.FirstName,
                            x.LastName,
                            x.EmployeeId),
                        EmailAddress = (x.EmailAddress ?? string.Empty).Trim()
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.EmailAddress))
                    .GroupBy(x => x.EmailAddress, StringComparer.OrdinalIgnoreCase)
                    .Select(g2 => g2.First())
                    .OrderBy(x => x.Name)
                    .ToList());

            var publishedRows = new List<DailyTicketAssignmentPublishedEntity>();

            foreach (var assignment in draftAssignments)
            {
                var ticket = ticketsById[assignment.TicketId];

                /*
                 * Publishing a Daily Assignment changes assignment ownership only.
                 * Ticket status is independent from technician/crew assignment.
                 */
                if (!protectedStatusNames.Contains(ticket.Status ?? ""))
                {
                    ticket.AssignedTech = BuildPublishedAssignedTechText(
                        assignment,
                        trucksById,
                        truckTechNamesByTruckId,
                        techniciansById);

                    ticket.AssignedCrewId = assignment.CrewId;
                    ticket.LastActivityAt = now;
                }

                assignment.IsPublished = true;
                assignment.PublishedVersion = nextPublishedVersion;
                assignment.PublishedAt = now;
                assignment.PublishedBy = publishedBy;
                assignment.UpdatedAt = now;
                assignment.UpdatedBy = publishedBy;

                publishedRows.Add(new DailyTicketAssignmentPublishedEntity
                {
                    AssignmentDate = assignment.AssignmentDate,
                    PublishedVersion = nextPublishedVersion,

                    TicketId = assignment.TicketId,
                    SourceAssignmentId = assignment.Id,

                    TargetType = assignment.TargetType,
                    TruckId = assignment.TruckId,
                    TechnicianId = assignment.TechnicianId,
                    CrewId = assignment.CrewId,

                    SortOrder = assignment.SortOrder,
                    AssignmentNotes = assignment.AssignmentNotes,

                    PublishedAt = now,
                    PublishedBy = publishedBy
                });
            }

            foreach (var ticketId in ticketsToUnassign)
            {
                var ticket = ticketsById[ticketId];

                ticket.AssignedTech = "(Unassigned)";
                ticket.AssignedCrewId = null;
                ticket.LastActivityAt = now;
            }

            if (draftAssignments.Count == 0)
            {
                /*
                 * Published assignment rows are permanent snapshots.
                 *
                 * An empty current route must not delete previous publications.
                 * Active/Removed/Completed state on the source assignment determines
                 * whether old published rows are still actionable by Field Tech.
                 */
                await _db.SaveChangesAsync(ct);

                return Ok(new PublishDailyAssignmentTargetResponse
                {
                    WorkDate = rosterDate,
                    TargetType = cleanTargetType,
                    TruckId = truckId == null
                        ? null
                        : (int?)truckId.Value,
                    TechnicianId = technicianId == null
                        ? null
                        : (int?)technicianId.Value,
                    PublishedCount = 0,
                    PublishedVersion = nextPublishedVersion,
                    PublishedAt = now,
                    PublishedBy = publishedBy,

                    EmailStatus = "Skipped",
                    EmailMessage =
                        "No tickets are currently published for this target. Email was not sent."
                });
            }
            /*
             * Every publish is an immutable historical snapshot.
             *
             * Do NOT replace or delete earlier published versions.
             * Field Technician loading uses the latest applicable version,
             * while older versions remain available for audit and
             * "Changes Since Previous Publish" comparisons.
             */
            _db.DailyTicketAssignmentPublished.AddRange(
                publishedRows);

            await _db.SaveChangesAsync(ct);

            var emailResult = await TrySendDailyAssignmentPublishedEmailAsync(
                rosterDate,
                publishedBy,
                cleanTargetType,
                truckId,
                technicianId,
                nextPublishedVersion,
                now,
                isModifiedPublish,
                previousPublishedRowsForEmail,
                publishedRows,
                ticketsById,
                trucksById,
                truckTechNamesByTruckId,
                techniciansById,
                truckEmailRecipientsByTruckId,
                technicianEmailRecipientsById,
                ct);

            return Ok(new PublishDailyAssignmentTargetResponse
            {
                WorkDate = rosterDate,

                TargetType = cleanTargetType,
                TruckId = truckId == null ? null : (int?)truckId.Value,
                TechnicianId = technicianId == null ? null : (int?)technicianId.Value,

                PublishedVersion = nextPublishedVersion,
                PublishedAt = now,
                PublishedBy = publishedBy,

                PublishedCount = publishedRows.Count,
                TicketIds = currentTicketIds.OrderBy(x => x).ToList(),

                EmailLogId = emailResult.LogId,
                EmailStatus = emailResult.Status,
                EmailMessage = emailResult.Message
            });
        }

        private static DailyAssignmentTicketDto MapTicketPoolItem(TicketEntity ticket, Dictionary<long, DailyTicketAssignmentEntity> assignmentByTicketId,
            HashSet<string> closedStatusNames, HashSet<string> fieldCompleteStatusNames)
        {
            assignmentByTicketId.TryGetValue(ticket.Id, out var assignment);

            return new DailyAssignmentTicketDto
            {
                TicketId = ticket.Id,

                Site = ticket.Site ?? "",
                NotificationName = ticket.NotificationName ?? "",
                Notification = ticket.Notification ?? "",

                Status = string.IsNullOrWhiteSpace(ticket.Status) ? "Open" : ticket.Status,
                IsClosed = closedStatusNames.Contains(ticket.Status ?? ""),
                IsFieldComplete = fieldCompleteStatusNames.Contains(ticket.Status ?? ""),

                AssignedTech = ticket.AssignedTech ?? "",

                CreatedAt = ticket.CreatedAt,
                LastActivityAt = ticket.LastActivityAt,

                WorkOrder = ticket.CurrentWorkOrder ?? "",
                WorkOrderClass = NormalizeWorkOrderType(ticket.WorkOrderClass),

                GroupCode = ticket.GroupCode ?? "",
                PriorityDays = ticket.PriorityDays,

                Problem = ticket.Problem ?? "",
                Notes = ticket.Notes ?? "",
                DispatchNotes = ticket.DispatchNotes ?? "",

                TaskCategoryId = ticket.TaskCategoryId,
                TaskCategoryName = ticket.TaskCategory?.Name,
                ActionRequiredOverride = ticket.ActionRequiredOverride,

                CurrentAssignmentId = assignment?.Id,
                CurrentAssignmentTargetType = assignment?.TargetType,
                CurrentAssignmentTruckId = assignment?.TruckId == null ? null : (int?)assignment.TruckId.Value,
                CurrentAssignmentTruckNumber = assignment?.Truck?.TruckNumber,
                CurrentAssignmentTechnicianId = assignment?.TechnicianId == null ? null : (int?)assignment.TechnicianId.Value,
                CurrentAssignmentTechnicianName = assignment?.Technician == null
                    ? null
                    : FormatTechnicianName(
                        assignment.Technician.FirstName,
                        assignment.Technician.LastName,
                        assignment.Technician.EmployeeId),
                CurrentAssignmentSortOrder = assignment?.SortOrder ?? 0,
                CurrentAssignmentIsPublished = assignment?.IsPublished ?? false
            };
        }

        private static DailyAssignedTicketDto MapAssignedTicket(DailyTicketAssignmentEntity assignment, HashSet<string> closedStatusNames,
            HashSet<string> fieldCompleteStatusNames)
        {
            var ticket = assignment.Ticket;

            return new DailyAssignedTicketDto
            {
                AssignmentId = assignment.Id,

                TicketId = ticket?.Id ?? assignment.TicketId,

                Site = ticket?.Site ?? "",
                NotificationName = ticket?.NotificationName ?? "",
                Notification = ticket?.Notification ?? "",

                Status = string.IsNullOrWhiteSpace(ticket?.Status) ? "Open" : ticket!.Status,
                IsClosed = closedStatusNames.Contains(ticket?.Status ?? ""),
                IsFieldComplete = fieldCompleteStatusNames.Contains(ticket?.Status ?? ""),

                AssignedTech = ticket?.AssignedTech ?? "",

                CreatedAt = ticket?.CreatedAt ?? default,
                LastActivityAt = ticket?.LastActivityAt ?? default,

                WorkOrder = ticket?.CurrentWorkOrder ?? "",
                WorkOrderClass = NormalizeWorkOrderType(ticket?.WorkOrderClass),

                GroupCode = ticket?.GroupCode ?? "",
                PriorityDays = ticket?.PriorityDays ?? 0,

                Problem = ticket?.Problem ?? "",
                Notes = ticket?.Notes ?? "",
                DispatchNotes = ticket?.DispatchNotes ?? "",

                TaskCategoryId = ticket?.TaskCategoryId,
                TaskCategoryName = ticket?.TaskCategory?.Name,
                ActionRequiredOverride = ticket?.ActionRequiredOverride,

                CurrentAssignmentId = assignment.Id,
                CurrentAssignmentTargetType = assignment.TargetType,
                CurrentAssignmentTruckId = assignment.TruckId == null ? null : (int?)assignment.TruckId.Value,
                CurrentAssignmentTruckNumber = assignment.Truck?.TruckNumber,
                CurrentAssignmentTechnicianId = assignment.TechnicianId == null ? null : (int?)assignment.TechnicianId.Value,
                CurrentAssignmentTechnicianName = assignment.Technician == null
                    ? null
                    : FormatTechnicianName(
                        assignment.Technician.FirstName,
                        assignment.Technician.LastName,
                        assignment.Technician.EmployeeId),
                CurrentAssignmentSortOrder = assignment.SortOrder,
                CurrentAssignmentIsPublished = assignment.IsPublished,

                TargetType = assignment.TargetType,
                TruckId = assignment.TruckId == null ? null : (int?)assignment.TruckId.Value,
                TechnicianId = assignment.TechnicianId == null ? null : (int?)assignment.TechnicianId.Value,
                CrewId = assignment.CrewId == null ? null : (int?)assignment.CrewId.Value,

                SortOrder = assignment.SortOrder,

                IsPublished = assignment.IsPublished,
                PublishedVersion = assignment.PublishedVersion,
                PublishedAt = assignment.PublishedAt,
                PublishedBy = assignment.PublishedBy,

                CarriedFromAssignmentId = assignment.CarriedFromAssignmentId,

                AssignmentNotes = assignment.AssignmentNotes ?? ""
            };
        }

        private static DailyAssignmentTechnicianDto MapTechnician(TechnicianEntity tech, DateTime workDate, int? truckId,
            string? truckNumber)
        {
            return new DailyAssignmentTechnicianDto
            {
                Id = (int)tech.Id,
                EmployeeId = tech.EmployeeId,

                FirstName = tech.FirstName,
                LastName = tech.LastName,
                Name = FormatTechnicianName(tech.FirstName, tech.LastName, tech.EmployeeId),

                Title = tech.Title,
                ScheduleText = GetScheduleText(tech),

                IsActive = tech.IsActive,
                IsOnShift = GetDefaultWorkingStatus(tech, workDate.DayOfWeek),

                TruckId = truckId,
                TruckNumber = truckNumber
            };
        }

        private IQueryable<TechnicianEntity> ActiveFieldTechniciansQuery()
        {
            return _db.Technicians
                .AsNoTracking()
                .Where(t =>
                    t.IsActive &&
                    t.TechnicianRoles.Any(tr =>
                        tr.Role.Code == TechnicianRoleCode ||
                        tr.Role.Code == LinemanRoleCode));
        }

        private static TechnicianEntity? PickLeadTechnician(IReadOnlyList<TechnicianEntity> technicians, uint truckId, uint? crewLeadTechnicianId)
        {
            if (technicians.Count == 0)
                return null;

            if (crewLeadTechnicianId.HasValue)
            {
                var crewLead = technicians.FirstOrDefault(t => t.Id == crewLeadTechnicianId.Value);

                if (crewLead != null)
                    return crewLead;
            }

            var homeTruckLead = technicians.FirstOrDefault(t => t.HomeTruckId == truckId);

            if (homeTruckLead != null)
                return homeTruckLead;

            return technicians
                .OrderByDescending(GetTitleRank)
                .ThenBy(t => t.LastName)
                .ThenBy(t => t.FirstName)
                .FirstOrDefault();
        }

        private static int GetTitleRank(TechnicianEntity tech)
        {
            var title = (tech.Title ?? string.Empty).Trim();

            if (title.Equals("Supervisor", StringComparison.OrdinalIgnoreCase))
                return 400;

            if (title.Equals("Head Journeyman", StringComparison.OrdinalIgnoreCase))
                return 300;

            if (title.Equals("Journeyman", StringComparison.OrdinalIgnoreCase))
                return 200;

            if (title.Equals("Apprentice", StringComparison.OrdinalIgnoreCase))
                return 100;

            return 0;
        }

        private static DateTime ParseDateOrToday(string? date)
            => (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
                ? parsed.Date
                : DateTime.Today.Date;

        private static bool IsTargetType(string? actual, string expected)
            => string.Equals(
                (actual ?? string.Empty).Trim(),
                expected,
                StringComparison.OrdinalIgnoreCase);

        private static string FormatTechnicianName(string? firstName, string? lastName, string? fallbackEmployeeId)
        {
            var fullName = $"{firstName ?? string.Empty} {lastName ?? string.Empty}".Trim();

            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName;

            return (fallbackEmployeeId ?? "Unknown").Trim();
        }

        private static bool GetDefaultWorkingStatus(TechnicianEntity t, DayOfWeek day)
            => day switch
            {
                DayOfWeek.Monday => t.WorksMonday,
                DayOfWeek.Tuesday => t.WorksTuesday,
                DayOfWeek.Wednesday => t.WorksWednesday,
                DayOfWeek.Thursday => t.WorksThursday,
                DayOfWeek.Friday => t.WorksFriday,
                DayOfWeek.Saturday => t.WorksSaturday,
                DayOfWeek.Sunday => t.WorksSunday,
                _ => false
            };

        private static string GetScheduleText(TechnicianEntity t)
        {
            var days = new List<string>();

            if (t.WorksMonday) days.Add("Mon");
            if (t.WorksTuesday) days.Add("Tues");
            if (t.WorksWednesday) days.Add("Wed");
            if (t.WorksThursday) days.Add("Thurs");
            if (t.WorksFriday) days.Add("Fri");
            if (t.WorksSaturday) days.Add("Sat");
            if (t.WorksSunday) days.Add("Sun");

            if (days.Count == 0)
                return "No scheduled days";

            if (t.WorksMonday && t.WorksTuesday && t.WorksWednesday && t.WorksThursday && t.WorksFriday &&
                !t.WorksSaturday && !t.WorksSunday)
                return "Mon-Fri";

            if (t.WorksMonday && t.WorksTuesday && t.WorksWednesday && t.WorksThursday &&
                !t.WorksFriday && !t.WorksSaturday && !t.WorksSunday)
                return "Mon-Thurs";

            if (!t.WorksMonday && t.WorksTuesday && t.WorksWednesday && t.WorksThursday && t.WorksFriday &&
                !t.WorksSaturday && !t.WorksSunday)
                return "Tues-Fri";

            return string.Join(", ", days);
        }

        private static string NormalizeWorkOrderType(string? workOrderClass)
        {
            var value = (workOrderClass ?? string.Empty).Trim();

            if (value.Equals("Cap", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Capital", StringComparison.OrdinalIgnoreCase))
                return "Capital";

            if (value.Equals("Maint", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
                return "Maintenance";

            if (value.Equals("Dist", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Distribution", StringComparison.OrdinalIgnoreCase))
                return "Distribution";

            return "";
        }

        private static string BuildPublishedAssignedTechText(DailyTicketAssignmentEntity assignment, Dictionary<uint, TruckEntity> trucksById,
            Dictionary<uint, List<string>> truckTechNamesByTruckId, Dictionary<uint, TechnicianEntity> techniciansById)
        {
            /*
             * A Technician target carrying TruckId is a crew assignment anchored to
             * its lead technician. Assigned To should show the crew names only.
             *
             * This also handles any remaining legacy Truck-target assignments.
             */
            if (assignment.TruckId.HasValue)
            {
                var truckId = assignment.TruckId.Value;

                if (truckTechNamesByTruckId.TryGetValue(truckId, out var techNames) &&
                    techNames.Count > 0)
                {
                    return FormatCrewDisplayText(techNames);
                }

                /*
                 * Fallback for an unexpected roster/context issue: prefer the lead
                 * technician name rather than writing a truck number into Assigned To.
                 */
                if (assignment.TechnicianId.HasValue &&
                    techniciansById.TryGetValue(assignment.TechnicianId.Value, out var crewLead))
                {
                    return FormatTechnicianName(
                        crewLead.FirstName,
                        crewLead.LastName,
                        crewLead.EmployeeId);
                }

                if (trucksById.TryGetValue(truckId, out var truck) &&
                    !string.IsNullOrWhiteSpace(truck.TruckNumber))
                {
                    return $"Truck {truck.TruckNumber.Trim()}";
                }
            }

            if (IsTargetType(assignment.TargetType, "Technician") &&
                assignment.TechnicianId.HasValue)
            {
                var technicianId = assignment.TechnicianId.Value;

                if (techniciansById.TryGetValue(technicianId, out var technician))
                {
                    return FormatTechnicianName(
                        technician.FirstName,
                        technician.LastName,
                        technician.EmployeeId);
                }

                return $"Technician {technicianId}";
            }

            return "(Unassigned)";
        }

        private static string FormatCrewDisplayText(IReadOnlyList<string> names)
        {
            var cleanNames = names
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanNames.Count == 0)
                return "Unknown";

            if (cleanNames.Count == 1)
                return cleanNames[0];

            if (cleanNames.Count == 2)
                return $"{cleanNames[0]} & {cleanNames[1]}";

            return string.Join(", ", cleanNames.Take(cleanNames.Count - 1)) +
                   " & " +
                   cleanNames.Last();
        }

        private static string NormalizeTargetType(string? targetType)
        {
            var value = (targetType ?? string.Empty).Trim();

            if (value.Equals("Truck", StringComparison.OrdinalIgnoreCase))
                return "Truck";

            if (value.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                return "Technician";

            return "";
        }

        private sealed class DailyAssignmentEmailRecipient
        {
            public string Name { get; set; } = "";

            public string EmailAddress { get; set; } = "";
        }

        private async Task<EmailSendResult> TrySendDailyAssignmentPublishedEmailAsync(
            DateTime workDate,
            string publishedBy,
            string targetType,
            uint? truckId,
            uint? technicianId,
            int publishedVersion,
            DateTime publishedAt,
            bool isModifiedPublish,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> previousPublishedRows,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> publishedRows,
            IReadOnlyDictionary<long, TicketEntity> ticketsById,
            IReadOnlyDictionary<uint, TruckEntity> trucksById,
            IReadOnlyDictionary<uint, List<string>> truckTechNamesByTruckId,
            IReadOnlyDictionary<uint, TechnicianEntity> techniciansById,
            IReadOnlyDictionary<uint, List<DailyAssignmentEmailRecipient>> truckEmailRecipientsByTruckId,
            IReadOnlyDictionary<uint, DailyAssignmentEmailRecipient> technicianEmailRecipientsById,
            CancellationToken ct)
        {
            try
            {
                var currentPublishedRows = publishedRows
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();

                var currentTicketIdSet = currentPublishedRows
                    .Select(x => x.TicketId)
                    .Distinct()
                    .ToHashSet();

                var currentTicketsById = ticketsById
                    .Where(x => currentTicketIdSet.Contains(x.Key))
                    .ToDictionary(x => x.Key, x => x.Value);

                var recipients = ResolveDailyAssignmentEmailRecipients(
                    truckId,
                    technicianId,
                    truckEmailRecipientsByTruckId,
                    technicianEmailRecipientsById);

                var targetDisplay = BuildDailyAssignmentEmailTargetDisplay(
                    targetType,
                    truckId,
                    technicianId,
                    trucksById,
                    truckTechNamesByTruckId,
                    techniciansById);

                var truckNumberDisplay = ResolveTruckNumberDisplay(
                    truckId,
                    trucksById);

                var publishedByDisplayName = await ResolvePublishedByDisplayNameAsync(
                    publishedBy,
                    ct);

                /*
                 * Route comparison and email sequencing are separate concerns.
                 *
                 * isModifiedPublish tells us whether a previous route snapshot exists,
                 * which controls whether the Changes Since Previous Publish section appears.
                 *
                 * EmailSequence tells us whether an assignment email has already been
                 * successfully sent to this target today.
                 */
                var emailSequence =
                    await _dailyAssignmentEmailSequence.GetNextAsync(
                        targetDisplay,
                        workDate,
                        ct);

                var emailTitle =
                    emailSequence.Title;

                var changeSummaryHtml =
                    isModifiedPublish
                        ? BuildDailyAssignmentChangeSummaryHtml(
                            previousPublishedRows,
                            currentPublishedRows,
                            ticketsById)
                        : "";

                var subject =
                    $"{targetDisplay} - " +
                    $"{emailTitle} - " +
                    $"{workDate:MM/dd/yyyy}";

                var body = BuildDailyAssignmentPublishedEmailBody(
                    workDate,
                    targetDisplay,
                    truckNumberDisplay,
                    publishedByDisplayName,
                    publishedAt,
                    emailTitle,
                    changeSummaryHtml,
                    currentPublishedRows,
                    currentTicketsById);

                var ticketIds = currentPublishedRows
                    .Select(x => x.TicketId)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                TicketEntity? onlyTicket = null;

                if (ticketIds.Count == 1)
                    currentTicketsById.TryGetValue(ticketIds[0], out onlyTicket);

                var allEmailsAddress = await GetAllEmailsAddressAsync(ct);

                return await _emailService.SendAsync(
                    new EmailSendRequest
                    {
                        EmailType = "DailyAssignment",

                        ToAddresses = recipients
                            .Select(x => x.EmailAddress)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),

                        CcAddresses = string.IsNullOrWhiteSpace(allEmailsAddress)
                            ? Array.Empty<string>()
                            : new[] { allEmailsAddress },

                        Subject = subject,
                        Body = body,
                        IsHtml = true,

                        CreatedBy = publishedBy,

                        RelatedTicketId = ticketIds.Count == 1
                            ? ticketIds[0]
                            : null,

                        RelatedSite = onlyTicket?.Site
                    },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Daily Assignment publish email failed. TargetType={TargetType}, TruckId={TruckId}, TechnicianId={TechnicianId}",
                    targetType,
                    truckId,
                    technicianId);

                return new EmailSendResult
                {
                    Status = "Failed",
                    Message = ex.Message
                };
            }
        }

        private static List<DailyAssignmentEmailRecipient> ResolveDailyAssignmentEmailRecipients(
            uint? truckId,
            uint? technicianId,
            IReadOnlyDictionary<uint, List<DailyAssignmentEmailRecipient>> truckEmailRecipientsByTruckId,
            IReadOnlyDictionary<uint, DailyAssignmentEmailRecipient> technicianEmailRecipientsById)
        {
            var recipients = new List<DailyAssignmentEmailRecipient>();

            if (truckId.HasValue &&
                truckEmailRecipientsByTruckId.TryGetValue(truckId.Value, out var truckRecipients))
            {
                recipients.AddRange(truckRecipients);
            }

            if (recipients.Count == 0 &&
                technicianId.HasValue &&
                technicianEmailRecipientsById.TryGetValue(technicianId.Value, out var technicianRecipient))
            {
                recipients.Add(technicianRecipient);
            }

            return recipients
                .Where(x => !string.IsNullOrWhiteSpace(x.EmailAddress))
                .GroupBy(x => x.EmailAddress.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Name)
                .ToList();
        }

        private static string BuildDailyAssignmentEmailTargetDisplay(
            string targetType,
            uint? truckId,
            uint? technicianId,
            IReadOnlyDictionary<uint, TruckEntity> trucksById,
            IReadOnlyDictionary<uint, List<string>> truckTechNamesByTruckId,
            IReadOnlyDictionary<uint, TechnicianEntity> techniciansById)
        {
            if (truckId.HasValue &&
                truckTechNamesByTruckId.TryGetValue(truckId.Value, out var techNames) &&
                techNames.Count > 0)
            {
                return FormatCrewDisplayText(techNames);
            }

            if (technicianId.HasValue &&
                techniciansById.TryGetValue(technicianId.Value, out var technician))
            {
                return FormatTechnicianName(
                    technician.FirstName,
                    technician.LastName,
                    technician.EmployeeId);
            }

            if (truckId.HasValue &&
                trucksById.TryGetValue(truckId.Value, out var truck))
            {
                return $"Truck {truck.TruckNumber}".Trim();
            }

            return string.IsNullOrWhiteSpace(targetType)
                ? "Daily Assignment Target"
                : targetType;
        }

        private static string BuildDailyAssignmentPublishedEmailBody(
            DateTime workDate,
            string targetDisplay,
            string truckNumberDisplay,
            string publishedBy,
            DateTime publishedAt,
            string emailTitle,
            string changeSummaryHtml,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> publishedRows,
            IReadOnlyDictionary<long, TicketEntity> ticketsById)
        {
            static string H(string? value)
                => WebUtility.HtmlEncode((value ?? string.Empty).Trim());

            static string DashIfBlank(string? value)
            {
                var clean = (value ?? string.Empty).Trim();

                return string.IsNullOrWhiteSpace(clean)
                    ? "—"
                    : WebUtility.HtmlEncode(clean);
            }
            var truckRowHtml = string.IsNullOrWhiteSpace(truckNumberDisplay)
                ? ""
                : $$"""
                        <tr>
                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Truck</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(truckNumberDisplay)}}</td>
                        <td></td>
                        <td></td>
                        </tr>
                    """;

            var sb = new StringBuilder();

            sb.AppendLine($$"""
                <!DOCTYPE html>
                <html>
                <body style="margin:0; padding:0; background:#f3f4f6; font-family:Segoe UI, Arial, sans-serif; color:#111827;">
                  <div style="max-width:1100px; margin:0 auto; padding:24px;">
                    <div style="background:#ffffff; border:1px solid #d1d5db; border-radius:12px; overflow:hidden;">
                      <div style="background:#1f2937; color:#ffffff; padding:18px 22px;">
                        <div style="font-size:22px; font-weight:700;">{{H(emailTitle)}}</div>
                      </div>
                """);

            sb.AppendLine($$"""
                <div style="padding:18px 22px;">
                <table cellpadding="0" cellspacing="0" style="width:100%; margin-bottom:18px; border-collapse:collapse;">
                    <tr>
                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Date</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{workDate:MM/dd/yyyy}}</td>

                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Assigned To</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 0;">{{H(targetDisplay)}}</td>
                    </tr>
                    <tr>
                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Published By</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(publishedBy)}}</td>

                    <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Published At</td>
                    <td style="font-size:14px; font-weight:600; padding:3px 0;">{{publishedAt:MM/dd/yyyy HH:mm}}</td>
                    </tr>
                    {{truckRowHtml}}
                </table>

                {{changeSummaryHtml}}

                <div style="font-size:15px; font-weight:700; margin:0 0 8px 0;">Current Route</div>

                <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db;">
                  <thead>
                    <tr style="background:#e5e7eb;">
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">#</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Site</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification Name</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Problem</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Work Order</th>
                      <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">WO Type</th>
                    </tr>
                  </thead>
                  <tbody>
                """);

            var rowNumber = 0;

            foreach (var assignment in publishedRows
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.Id))
            {
                if (!ticketsById.TryGetValue(assignment.TicketId, out var ticket))
                    continue;

                rowNumber++;

                var background = rowNumber % 2 == 0
                    ? "#f9fafb"
                    : "#ffffff";

                sb.AppendLine($$"""
                    <tr style="background:{{background}};">
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:600;">{{rowNumber}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:700;">{{DashIfBlank(ticket.Site)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.NotificationName)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.Problem)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.Notification)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.CurrentWorkOrder)}}</td>
                      <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(NormalizeWorkOrderType(ticket.WorkOrderClass))}}</td>
                    </tr>
                    """);

                var assignmentNotes = (assignment.AssignmentNotes ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(assignmentNotes))
                {
                    sb.AppendLine($$"""
                        <tr style="background:{{background}};">
                            <td style="font-size:12px; padding:8px 10px; border:1px solid #d1d5db;"></td>
                            <td colspan="6" style="font-size:12px; padding:8px 10px; border:1px solid #d1d5db; color:#374151;">
                            <strong>Assignment Notes:</strong> {{H(assignmentNotes)}}
                            </td>
                        </tr>
                        """);
                }
            }

            if (rowNumber == 0)
            {
                sb.AppendLine("""
                    <tr>
                        <td colspan="7" style="font-size:13px; padding:14px 10px; border:1px solid #d1d5db; color:#6b7280; font-style:italic;">
                        No ticket details were available.
                        </td>
                    </tr>
                    """);
            }

            sb.AppendLine("""
                          </tbody>
                        </table>

                        <div style="font-size:12px; color:#6b7280; margin-top:16px;">
                          This message was generated by SmartGridSuite.
                        </div>
                      </div>
                    </div>
                  </div>
                </body>
                </html>
                """);

            return sb.ToString();
        }

        private static string BuildDailyAssignmentChangeSummaryHtml(
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> previousRows,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> currentRows,
            IReadOnlyDictionary<long, TicketEntity> ticketsById)
        {
            static string H(string? value)
                => WebUtility.HtmlEncode((value ?? string.Empty).Trim());

            static string TicketLabel(
                long ticketId,
                IReadOnlyDictionary<long, TicketEntity> ticketsById)
            {
                if (!ticketsById.TryGetValue(ticketId, out var ticket))
                    return $"Ticket {ticketId}";

                var site = (ticket.Site ?? string.Empty).Trim();
                var notificationName = (ticket.NotificationName ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(site) &&
                    !string.IsNullOrWhiteSpace(notificationName))
                {
                    return $"{site} - {notificationName}";
                }

                if (!string.IsNullOrWhiteSpace(site))
                    return site;

                if (!string.IsNullOrWhiteSpace(notificationName))
                    return notificationName;

                return $"Ticket {ticketId}";
            }

            static string FormatTicketList(
                IEnumerable<long> ticketIds,
                IReadOnlyDictionary<long, TicketEntity> ticketsById)
            {
                var labels = ticketIds
                    .Select(id => H(TicketLabel(id, ticketsById)))
                    .ToList();

                return labels.Count == 0
                    ? "—"
                    : string.Join("<br/>", labels);
            }

            var previousOrdered = previousRows
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .GroupBy(x => x.TicketId)
                .Select(g => g.First())
                .ToList();

            var currentOrdered = currentRows
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .GroupBy(x => x.TicketId)
                .Select(g => g.First())
                .ToList();

            var previousTicketIds = previousOrdered
                .Select(x => x.TicketId)
                .ToList();

            var currentTicketIds = currentOrdered
                .Select(x => x.TicketId)
                .ToList();

            var previousTicketIdSet = previousTicketIds.ToHashSet();
            var currentTicketIdSet = currentTicketIds.ToHashSet();

            var addedTicketIds = currentTicketIds
                .Where(id => !previousTicketIdSet.Contains(id))
                .ToList();

            var removedTicketIds = previousTicketIds
                .Where(id => !currentTicketIdSet.Contains(id))
                .ToList();

            var previousIndexByTicketId = previousTicketIds
                .Select((id, index) => new
                {
                    TicketId = id,
                    RouteOrder = index + 1
                })
                .ToDictionary(x => x.TicketId, x => x.RouteOrder);

            var currentIndexByTicketId = currentTicketIds
                .Select((id, index) => new
                {
                    TicketId = id,
                    RouteOrder = index + 1
                })
                .ToDictionary(x => x.TicketId, x => x.RouteOrder);

            var reorderedTicketIds = currentTicketIds
                .Where(id =>
                    previousIndexByTicketId.ContainsKey(id) &&
                    currentIndexByTicketId.ContainsKey(id) &&
                    previousIndexByTicketId[id] != currentIndexByTicketId[id])
                .ToList();

            var rows = new List<(string Change, string Details)>();

            if (addedTicketIds.Count > 0)
            {
                rows.Add((
                    $"Added ({addedTicketIds.Count})",
                    FormatTicketList(addedTicketIds, ticketsById)));
            }

            if (removedTicketIds.Count > 0)
            {
                rows.Add((
                    $"Removed ({removedTicketIds.Count})",
                    FormatTicketList(removedTicketIds, ticketsById)));
            }

            if (reorderedTicketIds.Count > 0)
            {
                var reorderedDetails = reorderedTicketIds
                    .Select(id =>
                        $"{H(TicketLabel(id, ticketsById))}: " +
                        $"#{previousIndexByTicketId[id]} → #{currentIndexByTicketId[id]}")
                    .ToList();

                rows.Add((
                    $"Route Order Changed ({reorderedTicketIds.Count})",
                    string.Join("<br/>", reorderedDetails)));
            }

            if (rows.Count == 0)
            {
                rows.Add((
                    "Republished",
                    "No ticket additions, removals, or route-order changes were detected."));
            }

            var sb = new StringBuilder();

            sb.AppendLine("""
                <div style="margin-bottom:18px;">
                  <div style="font-size:15px; font-weight:700; margin:0 0 8px 0;">Changes Since Previous Publish</div>

                  <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db;">
                    <thead>
                      <tr style="background:#e5e7eb;">
                        <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db; width:220px;">Change</th>
                        <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Details</th>
                      </tr>
                    </thead>
                    <tbody>
                """);

            foreach (var row in rows)
            {
                sb.AppendLine($$"""
                      <tr>
                        <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:700;">{{H(row.Change)}}</td>
                        <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{row.Details}}</td>
                      </tr>
                    """);
            }

            sb.AppendLine("""
                    </tbody>
                  </table>
                </div>
                """);

            return sb.ToString();
        }

        private static IQueryable<DailyTicketAssignmentEntity> FilterDraftTargetRows(
            IQueryable<DailyTicketAssignmentEntity> query, DateTime assignmentDate,
            string targetType, uint? truckId, uint? technicianId)
        {
            query = query.Where(x =>
                x.AssignmentDate == assignmentDate &&
                x.AssignmentStatus == AssignmentStatusActive &&
                x.TargetType == targetType);

            if (targetType.Equals("Technician", StringComparison.OrdinalIgnoreCase))
            {
                return query.Where(x => x.TechnicianId == technicianId);
            }

            return query.Where(x => x.TruckId == truckId);
        }

        private static IQueryable<DailyTicketAssignmentPublishedEntity> FilterPublishedTargetRows(
            IQueryable<DailyTicketAssignmentPublishedEntity> query, DateTime assignmentDate,
            string targetType, uint? truckId, uint? technicianId)
        {
            query = query.Where(x =>
                x.AssignmentDate == assignmentDate &&
                x.TargetType == targetType);

            if (targetType.Equals("Technician", StringComparison.OrdinalIgnoreCase))
            {
                return query.Where(x => x.TechnicianId == technicianId);
            }

            return query.Where(x => x.TruckId == truckId);
        }

        private async Task<string> BuildAssignmentConflictWarningMessageAsync(
            DateTime rosterDate,
            DateTime assignmentDate,
            string targetType,
            uint? truckId,
            uint? technicianId,
            CancellationToken ct)
        {
            /*
             * Only crew-context assignments need this warning.
             * Example: assigning work to Daniel's crew while Alex, a crew member, still
             * has individual or other-route work assigned.
             */
            if (!targetType.Equals("Technician", StringComparison.OrdinalIgnoreCase) ||
                !truckId.HasValue ||
                !technicianId.HasValue)
            {
                return "";
            }

            var targetOwnerTechnicianId = technicianId.Value;

            var crewTechnicians = await (
                from roster in _db.TruckRosters.AsNoTracking()
                join tech in ActiveFieldTechniciansQuery()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == rosterDate &&
                      roster.TruckId == truckId.Value
                select new
                {
                    tech.Id,
                    tech.EmployeeId,
                    tech.FirstName,
                    tech.LastName
                })
                .ToListAsync(ct);

            var nonLeadCrewTechnicians = crewTechnicians
                .Where(x => x.Id != targetOwnerTechnicianId)
                .ToList();

            if (nonLeadCrewTechnicians.Count == 0)
                return "";

            var nonLeadTechIds = nonLeadCrewTechnicians
                .Select(x => x.Id)
                .ToHashSet();

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

            var terminalStatusNames = statusRows
                .Where(x => x.IsClosed || x.IsFieldComplete)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var conflicts = new Dictionary<uint, List<string>>();

            void AddConflict(uint techId, string site)
            {
                if (string.IsNullOrWhiteSpace(site))
                    site = "(blank site)";

                if (!conflicts.TryGetValue(techId, out var list))
                {
                    list = new List<string>();
                    conflicts[techId] = list;
                }

                if (!list.Contains(site, StringComparer.OrdinalIgnoreCase))
                    list.Add(site);
            }

            /*
             * Draft/current assignment conflicts.
             * These are un-published or current Daily Assignment rows owned by non-lead
             * crew members.
             */
            var draftConflicts = await _db.DailyTicketAssignments
                .AsNoTracking()
                .Include(x => x.Ticket)
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.AssignmentStatus == AssignmentStatusActive &&
                    x.TargetType == "Technician" &&
                    x.TechnicianId.HasValue &&
                    nonLeadTechIds.Contains(x.TechnicianId.Value) &&
                    x.Ticket != null)
                .ToListAsync(ct);

            foreach (var row in draftConflicts)
            {
                var ticketStatus = row.Ticket?.Status ?? "";

                if (terminalStatusNames.Contains(ticketStatus))
                    continue;

                AddConflict(
                    row.TechnicianId!.Value,
                    row.Ticket?.Site ?? $"Ticket {row.TicketId}");
            }

            /*
             * Latest published route conflicts.
             * This catches cases where a non-lead tech still has a published individual
             * route even if their draft list was not currently selected in Dispatcher.
             */
            foreach (var techId in nonLeadTechIds)
            {
                var latestVersion = await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == assignmentDate &&
                        x.TargetType == "Technician" &&
                        x.TechnicianId == techId)
                    .Select(x => (int?)x.PublishedVersion)
                    .MaxAsync(ct);

                if (!latestVersion.HasValue)
                    continue;

                var publishedConflicts = await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Include(x => x.Ticket)
                    .Where(x =>
                        x.AssignmentDate == assignmentDate &&
                        x.TargetType == "Technician" &&
                        x.TechnicianId == techId &&
                        x.PublishedVersion == latestVersion.Value &&
                        x.Ticket != null)
                    .ToListAsync(ct);

                foreach (var row in publishedConflicts)
                {
                    var ticketStatus = row.Ticket?.Status ?? "";

                    if (terminalStatusNames.Contains(ticketStatus))
                        continue;

                    AddConflict(
                        techId,
                        row.Ticket?.Site ?? $"Ticket {row.TicketId}");
                }
            }

            if (conflicts.Count == 0)
                return "";

            var lines = new List<string>
            {
                "This crew includes technician(s) who already have active Daily Assignment work elsewhere:",
                ""
            };

            foreach (var tech in nonLeadCrewTechnicians
                         .Where(x => conflicts.ContainsKey(x.Id))
                         .OrderBy(x => x.LastName)
                         .ThenBy(x => x.FirstName))
            {
                var name = FormatTechnicianName(
                    tech.FirstName,
                    tech.LastName,
                    tech.EmployeeId);

                var sites = conflicts[tech.Id]
                    .OrderBy(x => x)
                    .ToList();

                var sitePreview = string.Join(", ", sites.Take(8));

                if (sites.Count > 8)
                    sitePreview += $", +{sites.Count - 8} more";

                lines.Add($"{name}: {sites.Count} ticket(s)");
                lines.Add($"Sites: {sitePreview}");
                lines.Add("");
            }

            lines.Add("Continue assignment anyway?");

            return string.Join(Environment.NewLine, lines).Trim();
        }

        private async Task<string> ResolvePublishedByDisplayNameAsync(
            string? publishedBy, CancellationToken ct)
        {
            var clean = (publishedBy ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(clean))
                return "Dispatcher";

            var technician = await _db.Technicians
                .AsNoTracking()
                .Where(x => x.EmployeeId == clean)
                .Select(x => new
                {
                    x.FirstName,
                    x.LastName,
                    x.EmployeeId
                })
                .FirstOrDefaultAsync(ct);

            if (technician == null)
                return clean;

            return FormatTechnicianName(
                technician.FirstName,
                technician.LastName,
                technician.EmployeeId);
        }

        private static string ResolveTruckNumberDisplay(uint? truckId, IReadOnlyDictionary<uint, TruckEntity> trucksById)
        {
            if (!truckId.HasValue)
                return "";

            if (!trucksById.TryGetValue(truckId.Value, out var truck))
                return "";

            var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(truckNumber)
                ? ""
                : $"Truck {truckNumber}";
        }

        private async Task<string> GetAllEmailsAddressAsync(CancellationToken ct)
        {
            var value = await _db.AppSettings
                .AsNoTracking()
                .Where(x => x.SettingKey == "Email.AllEmailsAddress")
                .Select(x => x.SettingValue)
                .FirstOrDefaultAsync(ct);

            return (value ?? string.Empty).Trim();
        }
    }
}