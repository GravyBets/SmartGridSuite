#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Dispatcher.DailyAssignments;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/daily-assignments")]
    public sealed class DailyAssignmentsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;

        private static readonly DateTime ActiveAssignmentDate = new(2000, 1, 1);

        public DailyAssignmentsController(SmartGridDbContext db)
        {
            _db = db;
        }

        [HttpGet("board")]
        public async Task<ActionResult<DailyAssignmentsBoardDto>> GetBoard([FromQuery] string? date = null, CancellationToken ct = default)
        {
            var rosterDate = ParseDateOrToday(date);
            var assignmentDate = ActiveAssignmentDate;

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

            var assignmentByTicketId = assignments
                .Where(x => x.Ticket != null)
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
                join tech in _db.Technicians.AsNoTracking()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == rosterDate && tech.IsActive
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

            var assignmentsByTruckId = assignments
                .Where(x => IsTargetType(x.TargetType, "Truck") && x.TruckId.HasValue)
                .GroupBy(x => x.TruckId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SortOrder)
                          .ThenBy(x => x.Id)
                          .Select(x => MapAssignedTicket(x, closedStatusNames, fieldCompleteStatusNames))
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

            var allActiveTechs = await _db.Technicians
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.LastName)
                .ThenBy(t => t.FirstName)
                .ToListAsync(ct);

            var technicianAssignmentsByTechId = assignments
                .Where(x => IsTargetType(x.TargetType, "Technician") && x.TechnicianId.HasValue)
                .GroupBy(x => x.TechnicianId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SortOrder)
                          .ThenBy(x => x.Id)
                          .Select(x => MapAssignedTicket(x, closedStatusNames, fieldCompleteStatusNames))
                          .ToList());

            var technicianTargetIds = allActiveTechs
                .Where(t => !assignedTruckNumberByTechId.ContainsKey(t.Id))
                .Select(t => t.Id)
                .Union(technicianAssignmentsByTechId.Keys)
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

            var truckTargets = trucks
                .Select(truck =>
                {
                    var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

                    CrewEntity? crew = null;

                    if (!string.IsNullOrWhiteSpace(truckNumber))
                        crewByTruckNumber.TryGetValue(truckNumber, out crew);

                    return new DailyAssignmentTargetDto
                    {
                        TargetKey = $"Truck:{truck.Id}",
                        TargetType = "Truck",

                        TruckId = (int)truck.Id,
                        TruckNumber = truckNumber,
                        TruckStyleName = truck.TruckStyle?.Name,

                        CrewId = crew == null ? null : (int?)crew.Id,

                        Technicians = techsByTruckId.TryGetValue(truck.Id, out var techs)
                            ? techs
                            : new List<DailyAssignmentTechnicianDto>(),

                        AssignedTickets = assignmentsByTruckId.TryGetValue(truck.Id, out var assigned)
                            ? assigned
                            : new List<DailyAssignedTicketDto>()
                    };
                })
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

                var technicianExists = await _db.Technicians
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == technicianId.Value && x.IsActive, ct);

                if (!technicianExists)
                    return NotFound($"Technician {req.TechnicianId.Value} was not found or is inactive.");
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

                var technicianExists = await _db.Technicians
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == technicianId.Value && x.IsActive, ct);

                if (!technicianExists)
                    return NotFound($"Technician {req.TechnicianId.Value} was not found or is inactive.");
            }

            var assignments = await _db.DailyTicketAssignments
                .Where(x =>
                    x.AssignmentDate == workDate &&
                    x.TargetType == cleanTargetType &&
                    x.TruckId == truckId &&
                    x.TechnicianId == technicianId)
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

        [HttpPost("publish")]
        public async Task<ActionResult<PublishDailyAssignmentsResponse>> PublishAssignments([FromBody] PublishDailyAssignmentsRequest req,
            CancellationToken ct)
        {
            var rosterDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;
            var workDate = ActiveAssignmentDate;

            var publishedBy = string.IsNullOrWhiteSpace(req.PublishedBy)
                ? "Dispatcher"
                : req.PublishedBy.Trim();

            var draftAssignments = await _db.DailyTicketAssignments
                .Where(x => x.AssignmentDate == workDate)
                .OrderBy(x => x.TargetType)
                .ThenBy(x => x.TruckId)
                .ThenBy(x => x.TechnicianId)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            if (draftAssignments.Count == 0)
                return BadRequest("There are no daily assignments to publish.");

            var ticketIds = draftAssignments
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            var ticketsById = tickets.ToDictionary(x => x.Id);

            var missingTicketIds = ticketIds
                .Where(id => !ticketsById.ContainsKey(id))
                .ToList();

            if (missingTicketIds.Count > 0)
                return NotFound($"One or more tickets were not found: {string.Join(", ", missingTicketIds)}");

            var nextPublishedVersion =
                (await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x => x.AssignmentDate == workDate)
                    .Select(x => (int?)x.PublishedVersion)
                    .MaxAsync(ct) ?? 0) + 1;

            var truckIds = draftAssignments
                .Where(x => x.TruckId.HasValue)
                .Select(x => x.TruckId.GetValueOrDefault())
                .Distinct()
                .ToList();

            var technicianIds = draftAssignments
                .Where(x => x.TechnicianId.HasValue)
                .Select(x => x.TechnicianId.GetValueOrDefault())
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

            var now = DateTime.Now;
            var publishedRows = new List<DailyTicketAssignmentPublishedEntity>();

            foreach (var assignment in draftAssignments)
            {
                var ticket = ticketsById[assignment.TicketId];

                ticket.AssignedTech = BuildPublishedAssignedTechText(
                    assignment,
                    trucksById,
                    truckTechNamesByTruckId,
                    techniciansById);

                ticket.AssignedCrewId = assignment.CrewId;
                ticket.LastActivityAt = now;

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

            _db.DailyTicketAssignmentPublished.AddRange(publishedRows);

            await _db.SaveChangesAsync(ct);

            return Ok(new PublishDailyAssignmentsResponse
            {
                WorkDate = workDate,
                PublishedVersion = nextPublishedVersion,
                PublishedAt = now,
                PublishedBy = publishedBy,
                PublishedCount = publishedRows.Count,
                TicketIds = ticketIds.OrderBy(x => x).ToList()
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

                var techExists = await _db.Technicians
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == technicianId.Value && x.IsActive, ct);

                if (!techExists)
                    return NotFound($"Technician {req.TechnicianId.Value} was not found or is inactive.");
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

            if (draftAssignments.Count == 0)
                return BadRequest("There are no assignments to publish for this crew/technician.");

            var ticketIds = draftAssignments
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            var ticketsById = tickets.ToDictionary(x => x.Id);

            var missingTicketIds = ticketIds
                .Where(id => !ticketsById.ContainsKey(id))
                .ToList();

            if (missingTicketIds.Count > 0)
                return NotFound($"One or more tickets were not found: {string.Join(", ", missingTicketIds)}");

            var nextPublishedVersion =
                (await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x => x.AssignmentDate == workDate)
                    .Select(x => (int?)x.PublishedVersion)
                    .MaxAsync(ct) ?? 0) + 1;

            var truckIds = draftAssignments
                .Where(x => x.TruckId.HasValue)
                .Select(x => x.TruckId.GetValueOrDefault())
                .Distinct()
                .ToList();

            var technicianIds = draftAssignments
                .Where(x => x.TechnicianId.HasValue)
                .Select(x => x.TechnicianId.GetValueOrDefault())
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

            var now = DateTime.Now;
            var publishedRows = new List<DailyTicketAssignmentPublishedEntity>();

            foreach (var assignment in draftAssignments)
            {
                var ticket = ticketsById[assignment.TicketId];

                ticket.AssignedTech = BuildPublishedAssignedTechText(
                    assignment,
                    trucksById,
                    truckTechNamesByTruckId,
                    techniciansById);

                ticket.AssignedCrewId = assignment.CrewId;
                ticket.LastActivityAt = now;

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
                TicketIds = ticketIds.OrderBy(x => x).ToList()
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

        private static string BuildPublishedAssignedTechText(DailyTicketAssignmentEntity assignment,
            Dictionary<uint, TruckEntity> trucksById,
            Dictionary<uint, List<string>> truckTechNamesByTruckId,
            Dictionary<uint, TechnicianEntity> techniciansById)
        {
            if (IsTargetType(assignment.TargetType, "Truck") && assignment.TruckId.HasValue)
            {
                var truckId = assignment.TruckId.Value;

                var truckNumber = trucksById.TryGetValue(truckId, out var truck)
                    ? (truck.TruckNumber ?? string.Empty).Trim()
                    : truckId.ToString();

                var displayTruck = string.IsNullOrWhiteSpace(truckNumber)
                    ? $"Truck {truckId}"
                    : $"Truck {truckNumber}";

                if (!truckTechNamesByTruckId.TryGetValue(truckId, out var techNames) || techNames.Count == 0)
                    return displayTruck;

                return $"{displayTruck} - {FormatCrewDisplayText(techNames)}";
            }

            if (IsTargetType(assignment.TargetType, "Technician") && assignment.TechnicianId.HasValue)
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