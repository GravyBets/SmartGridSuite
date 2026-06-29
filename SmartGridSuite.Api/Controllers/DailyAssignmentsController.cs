#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Dispatcher.DailyAssignments;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/daily-assignments")]
    public sealed class DailyAssignmentsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        private readonly TruckBoardInitializationService _truckBoardInitialization;

        private static readonly DateTime ActiveAssignmentDate = new(2000, 1, 1);
        private const string TechnicianRoleCode = "TECHNICIAN";

        public DailyAssignmentsController(SmartGridDbContext db, TruckBoardInitializationService truckBoardInitialization)
        {
            _db = db;
            _truckBoardInitialization = truckBoardInitialization;
        }

        [HttpGet("board")]
        public async Task<ActionResult<DailyAssignmentsBoardDto>> GetBoard([FromQuery] string? date = null, CancellationToken ct = default)
        {
            var rosterDate = ParseDateOrToday(date);
            var assignmentDate = ActiveAssignmentDate;

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
                .Where(x => x.AssignmentDate == assignmentDate)
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
            var rosterDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;
            var workDate = ActiveAssignmentDate;

            var cleanTargetType = (req.TargetType ?? string.Empty).Trim();

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
                .Where(x => x.AssignmentDate == workDate && ticketIds.Contains(x.TicketId))
                .ToListAsync(ct);

            var existingByTicketId = existingAssignments
                .GroupBy(x => x.TicketId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());

            var currentMaxSortOrder = await _db.DailyTicketAssignments
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == workDate &&
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

                if (existingByTicketId.TryGetValue(ticketId, out var assignment))
                {
                    assignment.TargetType = cleanTargetType;
                    assignment.TruckId = truckId;
                    assignment.TechnicianId = technicianId;
                    assignment.CrewId = crewId;
                    assignment.SortOrder = currentMaxSortOrder;

                    if (assignmentNotes != null)
                        assignment.AssignmentNotes = assignmentNotes;

                    assignment.IsPublished = false;
                    assignment.UpdatedAt = now;
                    assignment.UpdatedBy = updatedBy;

                    assignmentIds.Add(assignment.Id);
                }
                else
                {
                    var newAssignment = new DailyTicketAssignmentEntity
                    {
                        AssignmentDate = workDate,
                        TicketId = ticketId,

                        TargetType = cleanTargetType,
                        TruckId = truckId,
                        TechnicianId = technicianId,
                        CrewId = crewId,

                        SortOrder = currentMaxSortOrder,

                        IsPublished = false,
                        PublishedVersion = 0,
                        PublishedAt = null,
                        PublishedBy = null,

                        AssignmentNotes = assignmentNotes,

                        CreatedAt = now,
                        CreatedBy = updatedBy,
                        UpdatedAt = now,
                        UpdatedBy = updatedBy
                    };

                    _db.DailyTicketAssignments.Add(newAssignment);

                    await _db.SaveChangesAsync(ct);

                    assignmentIds.Add(newAssignment.Id);
                }
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
        public async Task<ActionResult<RemoveDailyTicketAssignmentsResponse>> RemoveAssignments([FromBody] RemoveDailyTicketAssignmentsRequest req,
            CancellationToken ct)
        {
            var workDate = ActiveAssignmentDate;

            var ticketIds = (req.TicketIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return BadRequest("At least one ticket is required.");

            var assignments = await _db.DailyTicketAssignments
                .Where(x => x.AssignmentDate == workDate && ticketIds.Contains(x.TicketId))
                .ToListAsync(ct);

            if (assignments.Count == 0)
            {
                return Ok(new RemoveDailyTicketAssignmentsResponse
                {
                    WorkDate = workDate,
                    RemovedCount = 0,
                    RemovedTicketIds = new List<long>()
                });
            }

            var removedTicketIds = assignments
                .Select(x => x.TicketId)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            _db.DailyTicketAssignments.RemoveRange(assignments);

            await _db.SaveChangesAsync(ct);

            return Ok(new RemoveDailyTicketAssignmentsResponse
            {
                WorkDate = workDate,
                RemovedCount = removedTicketIds.Count,
                RemovedTicketIds = removedTicketIds
            });
        }

        [HttpPost("migrate-truck-targets-to-lead-techs")]
        public async Task<IActionResult> MigrateTruckTargetAssignmentsToLeadTechs([FromQuery] string? date = null, CancellationToken ct = default)
        {
            var rosterDate = ParseDateOrToday(date);
            var assignmentDate = ActiveAssignmentDate;
            var now = DateTime.Now;

            var truckAssignments = await _db.DailyTicketAssignments
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
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
            var workDate = ActiveAssignmentDate;

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
                .Where(x => x.AssignmentDate == workDate)
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
            var rosterDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;
            var workDate = ActiveAssignmentDate;

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

            var draftAssignments = await _db.DailyTicketAssignments
                .Where(x =>
                    x.AssignmentDate == workDate &&
                    x.TargetType == cleanTargetType &&
                    x.TruckId == truckId &&
                    x.TechnicianId == technicianId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            var currentTicketIds = draftAssignments
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            /*
             * Find the last published list for this specific target.
             * Any ticket previously published here but no longer in the current draft
             * list has been removed from this target.
             */
            var previousTargetPublishedVersion = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == workDate &&
                    x.TargetType == cleanTargetType &&
                    x.TruckId == truckId &&
                    x.TechnicianId == technicianId)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct);

            var previouslyPublishedTicketIds = previousTargetPublishedVersion.HasValue
                ? await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.TargetType == cleanTargetType &&
                        x.TruckId == truckId &&
                        x.TechnicianId == technicianId &&
                        x.PublishedVersion == previousTargetPublishedVersion.Value)
                    .Select(x => x.TicketId)
                    .Distinct()
                    .ToListAsync(ct)
                : new List<long>();

            var releasedTicketIds = previouslyPublishedTicketIds
                .Except(currentTicketIds)
                .Distinct()
                .ToList();

            var nextPublishedVersion = (await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x => x.AssignmentDate == workDate)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct) ?? 0) + 1;

            var now = DateTime.Now;

            /*
             * Resolve configurable workflow statuses.
             * Assignment Target is needed whenever tickets are being published.
             * Unassignment Target is only needed when an actual non-terminal ticket
             * must be returned to unassigned.
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

            var assignmentTargetStatuses = statusRows
                .Where(x => x.IsAssignmentPublishTarget)
                .Select(x => x.Name)
                .ToList();

            if (currentTicketIds.Count > 0 && assignmentTargetStatuses.Count != 1)
            {
                return BadRequest(
                    "Exactly one active ticket status must be configured as the Assignment Target. " +
                    "Go to Administration > Tickets and select the status used when Daily Assignments are published.");
            }

            var assignmentTargetStatusName = assignmentTargetStatuses.SingleOrDefault();

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

            var missingCurrentTicketIds = currentTicketIds
                .Where(id => !ticketsById.ContainsKey(id))
                .ToList();

            if (missingCurrentTicketIds.Count > 0)
            {
                return NotFound(
                    $"One or more tickets were not found: {string.Join(", ", missingCurrentTicketIds)}");
            }

            /*
             * A ticket may have been moved to another target and published there
             * before this old target is republished. In that case, do not clear the
             * ticket's newly published assignment.
             */
            var publishedElsewhereTicketIds = releasedTicketIds.Count == 0
                ? new HashSet<long>()
                : (await _db.DailyTicketAssignments
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.IsPublished &&
                        releasedTicketIds.Contains(x.TicketId) &&
                        !(x.TargetType == cleanTargetType &&
                          x.TruckId == truckId &&
                          x.TechnicianId == technicianId))
                    .Select(x => x.TicketId)
                    .Distinct()
                    .ToListAsync(ct))
                    .ToHashSet();

            var ticketsToUnassign = releasedTicketIds
                .Where(id => ticketsById.ContainsKey(id))
                .Where(id => !publishedElsewhereTicketIds.Contains(id))
                .Where(id => !protectedStatusNames.Contains(ticketsById[id].Status ?? ""))
                .ToList();

            var unassignmentTargetStatuses = statusRows
                .Where(x => x.IsUnassignmentTarget)
                .Select(x => x.Name)
                .ToList();

            if (ticketsToUnassign.Count > 0 && unassignmentTargetStatuses.Count != 1)
            {
                return BadRequest(
                    "Exactly one active ticket status must be configured as the Unassignment Target. " +
                    "Go to Administration > Tickets and select the status used when a published ticket becomes unassigned.");
            }

            var unassignmentTargetStatusName = unassignmentTargetStatuses.SingleOrDefault();

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
                    tech.LastName
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

            var publishedRows = new List<DailyTicketAssignmentPublishedEntity>();

            foreach (var assignment in draftAssignments)
            {
                var ticket = ticketsById[assignment.TicketId];

                /*
                 * Do not push a completed/closed ticket back into Assigned just
                 * because its work list is republished.
                 */
                if (!protectedStatusNames.Contains(ticket.Status ?? ""))
                {
                    ticket.Status = assignmentTargetStatusName!;

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

                ticket.Status = unassignmentTargetStatusName!;
                ticket.AssignedTech = "(Unassigned)";
                ticket.AssignedCrewId = null;
                ticket.LastActivityAt = now;
            }

            if (draftAssignments.Count == 0)
            {
                /*
                 * Publishing an empty target removes that target's field-tech task list.
                 * The ticket records above are also returned to unassigned when eligible.
                 */
                var existingPublishedRows = await _db.DailyTicketAssignmentPublished
                    .Where(x =>
                        x.AssignmentDate == workDate &&
                        x.TargetType == cleanTargetType &&
                        x.TruckId == truckId &&
                        x.TechnicianId == technicianId)
                    .ToListAsync(ct);

                if (existingPublishedRows.Count > 0)
                    _db.DailyTicketAssignmentPublished.RemoveRange(existingPublishedRows);

                await _db.SaveChangesAsync(ct);

                return Ok(new PublishDailyAssignmentTargetResponse
                {
                    WorkDate = rosterDate,
                    TargetType = cleanTargetType,
                    TruckId = truckId == null ? null : (int?)truckId.Value,
                    TechnicianId = technicianId == null ? null : (int?)technicianId.Value,
                    PublishedCount = 0,
                    PublishedVersion = nextPublishedVersion,
                    PublishedAt = now,
                    PublishedBy = publishedBy
                });
            }

            _db.DailyTicketAssignmentPublished.AddRange(publishedRows);

            await _db.SaveChangesAsync(ct);

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
                TicketIds = currentTicketIds.OrderBy(x => x).ToList()
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
                    t.TechnicianRoles.Any(tr => tr.Role.Code == TechnicianRoleCode));
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
    }
}