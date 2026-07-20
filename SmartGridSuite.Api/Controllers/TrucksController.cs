#nullable enable
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Administration.Technicians;
using SmartGridSuite.Contracts.Administration.Trucks;
using SmartGridSuite.Api.Services;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TrucksController : ControllerBase
{
    private readonly SmartGridDbContext _db;
    private readonly TruckBoardInitializationService _truckBoardInitialization;

    private const string TechnicianRoleCode = "TECHNICIAN";

    public TrucksController(
        SmartGridDbContext db,
        TruckBoardInitializationService truckBoardInitialization)
    {
        _db = db;
        _truckBoardInitialization = truckBoardInitialization;
    }

    [HttpGet]
    public async Task<ActionResult<List<TruckDto>>> GetAll()
    {
        var items = await _db.Set<TruckEntity>()
            .AsNoTracking()
            .Include(t => t.TruckStyle)
            .OrderBy(t => t.TruckNumber)
            .Select(t => new TruckDto
            {
                Id = (int)t.Id,
                TruckNumber = t.TruckNumber,
                TruckStyleId = t.TruckStyleId == null ? null : (int?)t.TruckStyleId.Value,
                TruckStyleName = t.TruckStyle != null ? t.TruckStyle.Name : null,
                IsActive = t.IsActive,
                DisplayName = t.TruckStyle != null ? t.TruckStyle.Name : null
            })
            .ToListAsync();

        return items;
    }

    [HttpPost]
    public async Task<ActionResult<TruckDto>> Create([FromBody] CreateTruckRequest req)
    {
        var number = (req.TruckNumber ?? "").Trim();
        if (number.Length == 0)
            return BadRequest("TruckNumber is required.");

        var exists = await _db.Set<TruckEntity>()
            .AsNoTracking()
            .AnyAsync(t => t.TruckNumber == number);

        if (exists)
            return Conflict($"Truck '{number}' already exists.");

        uint? truckStyleId = req.TruckStyleId.HasValue ? (uint?)req.TruckStyleId.Value : null;

        if (truckStyleId.HasValue)
        {
            var styleExists = await _db.Set<TruckStyleEntity>()
                .AsNoTracking()
                .AnyAsync(s => s.Id == truckStyleId.Value && s.IsActive);

            if (!styleExists)
                return BadRequest($"TruckStyleId '{req.TruckStyleId}' was not found or is inactive.");
        }

        var entity = new TruckEntity
        {
            TruckNumber = number,
            TruckStyleId = truckStyleId,
            DisplayName = null,
            IsActive = req.IsActive
        };

        _db.Set<TruckEntity>().Add(entity);
        await _db.SaveChangesAsync();

        var styleName = truckStyleId == null
            ? null
            : await _db.Set<TruckStyleEntity>()
                .AsNoTracking()
                .Where(s => s.Id == truckStyleId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync();

        var dto = new TruckDto
        {
            Id = (int)entity.Id,
            TruckNumber = entity.TruckNumber,
            TruckStyleId = entity.TruckStyleId == null ? null : (int?)entity.TruckStyleId.Value,
            TruckStyleName = styleName,
            IsActive = entity.IsActive,
            DisplayName = styleName
        };

        return CreatedAtAction(nameof(GetAll), new { }, dto);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpdateTruckRequest req)
    {
        var entity = await _db.Set<TruckEntity>().FirstOrDefaultAsync(t => t.Id == (uint)id);
        if (entity == null)
            return NotFound();

        if (req.TruckNumber != null)
        {
            var number = req.TruckNumber.Trim();
            if (number.Length == 0)
                return BadRequest("TruckNumber cannot be empty.");

            var exists = await _db.Set<TruckEntity>().AsNoTracking()
                .AnyAsync(t => t.TruckNumber == number && t.Id != entity.Id);

            if (exists)
                return Conflict($"Truck '{number}' already exists.");

            entity.TruckNumber = number;
        }

        if (req.TruckStyleId.HasValue)
        {
            var truckStyleId = (uint)req.TruckStyleId.Value;

            var styleExists = await _db.Set<TruckStyleEntity>()
                .AsNoTracking()
                .AnyAsync(s => s.Id == truckStyleId && s.IsActive);

            if (!styleExists)
                return BadRequest($"TruckStyleId '{req.TruckStyleId}' was not found or is inactive.");

            entity.TruckStyleId = truckStyleId;
        }

        if (req.IsActive.HasValue)
            entity.IsActive = req.IsActive.Value;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var entity = await _db.Set<TruckEntity>()
            .FirstOrDefaultAsync(t => t.Id == (uint)id);

        if (entity == null)
            return NotFound();

        _db.Set<TruckEntity>().Remove(entity);

        try
        {
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException)
        {
            return Conflict(
                "This truck cannot be deleted because related records exist. " +
                "Take the truck out of service instead.");
        }
    }

    [HttpGet("board")]
    public async Task<ActionResult<TruckBoardDto>> GetBoard([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

        await _truckBoardInitialization.EnsureBoardInitializedAsync(workDate);

        var trucks = await _db.Set<TruckEntity>()
                .AsNoTracking()
            .Include(t => t.TruckStyle)
            .Where(t => t.IsActive)
            .OrderBy(t => t.TruckNumber)
            .Select(t => new TruckDto
            {
                Id = (int)t.Id,
                TruckNumber = t.TruckNumber,
                TruckStyleId = t.TruckStyleId == null ? null : (int?)t.TruckStyleId.Value,
                TruckStyleName = t.TruckStyle != null ? t.TruckStyle.Name : null,
                IsActive = t.IsActive,
                DisplayName = t.TruckStyle != null ? t.TruckStyle.Name : null
            })
            .ToListAsync();

        var rosterRows = await _db.Set<TruckRosterEntity>()
            .AsNoTracking()
            .Where(r => r.WorkDate == workDate)
            .Join(ActiveFieldTechniciansQuery(),
                r => r.TechnicianId,
                t => t.Id,
                (r, t) => new
                {
                    TruckId = (int)r.TruckId,
                    Tech = new TechnicianDto
                    {
                        Id = (int)t.Id,
                        EmployeeId = t.EmployeeId,
                        FirstName = t.FirstName,
                        LastName = t.LastName,
                        Name = ((t.FirstName ?? "") + " " + (t.LastName ?? "")).Trim(),
                        IsActive = t.IsActive,
                        Title = t.Title,
                        ScheduleText = GetScheduleText(t),
                        HomeTruckId = t.HomeTruckId == null ? null : (int?)t.HomeTruckId.Value,
                        HomeTruckNumber = null,
                        HomeTruckDisplayName = null,
                        WorksMonday = t.WorksMonday,
                        WorksTuesday = t.WorksTuesday,
                        WorksWednesday = t.WorksWednesday,
                        WorksThursday = t.WorksThursday,
                        WorksFriday = t.WorksFriday,
                        WorksSaturday = t.WorksSaturday,
                        WorksSunday = t.WorksSunday,
                        RoleCodes = new List<string>(),
                        IsOnShift = GetDefaultWorkingStatus(t, workDate.DayOfWeek),
                        TruckNumber = null
                    }
                })
            .ToListAsync();

        var techsByTruck = rosterRows
            .GroupBy(x => x.TruckId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Tech).OrderBy(x => x.Name).ToList());

        var unassigned = await ActiveFieldTechniciansQuery()
            .Where(t => !_db.Set<TruckRosterEntity>()
                .Any(r => r.WorkDate == workDate && r.TechnicianId == t.Id))
            .OrderBy(t => t.LastName)
            .ThenBy(t => t.FirstName)
            .Select(t => new TechnicianDto
            {
                Id = (int)t.Id,
                EmployeeId = t.EmployeeId,
                FirstName = t.FirstName,
                LastName = t.LastName,
                Name = ((t.FirstName ?? "") + " " + (t.LastName ?? "")).Trim(),
                IsActive = t.IsActive,
                Title = t.Title,
                ScheduleText = GetScheduleText(t),
                HomeTruckId = t.HomeTruckId == null ? null : (int?)t.HomeTruckId.Value,
                HomeTruckNumber = null,
                HomeTruckDisplayName = null,
                WorksMonday = t.WorksMonday,
                WorksTuesday = t.WorksTuesday,
                WorksWednesday = t.WorksWednesday,
                WorksThursday = t.WorksThursday,
                WorksFriday = t.WorksFriday,
                WorksSaturday = t.WorksSaturday,
                WorksSunday = t.WorksSunday,
                RoleCodes = new List<string>(),
                IsOnShift = GetDefaultWorkingStatus(t, workDate.DayOfWeek),
                TruckNumber = null
            })
            .ToListAsync();

        var assignedTruckNumberByTechId = rosterRows
            .Join(trucks,
                r => r.TruckId,
                t => t.Id,
                (r, t) => new
                {
                    r.Tech.Id,
                    t.TruckNumber
                })
                .GroupBy(x => x.Id)
                .ToDictionary(
                    g => g.Key,
                    g => g.First().TruckNumber);

        var allTechnicians = await ActiveFieldTechniciansQuery()
            .OrderBy(t => t.LastName)
            .ThenBy(t => t.FirstName)
            .Select(t => new TechnicianDto
            {
                Id = (int)t.Id,
                EmployeeId = t.EmployeeId,
                FirstName = t.FirstName,
                LastName = t.LastName,
                Name = ((t.FirstName ?? "") + " " + (t.LastName ?? "")).Trim(),
                Title = t.Title,
                ScheduleText = GetScheduleText(t),
                IsActive = t.IsActive,
                HomeTruckId = t.HomeTruckId == null ? null : (int?)t.HomeTruckId.Value,
                HomeTruckNumber = null,
                HomeTruckDisplayName = null,
                WorksMonday = t.WorksMonday,
                WorksTuesday = t.WorksTuesday,
                WorksWednesday = t.WorksWednesday,
                WorksThursday = t.WorksThursday,
                WorksFriday = t.WorksFriday,
                WorksSaturday = t.WorksSaturday,
                WorksSunday = t.WorksSunday,
                RoleCodes = new List<string>(),
                IsOnShift = GetDefaultWorkingStatus(t, workDate.DayOfWeek),
                TruckNumber = null
            })
            .ToListAsync();

        foreach (var tech in allTechnicians)
        {
            if (assignedTruckNumberByTechId.TryGetValue(tech.Id, out var truckNumber))
                tech.TruckNumber = truckNumber;
        }

        var truckNumberById = trucks
            .ToDictionary(
        x => x.Id,
        x => (x.TruckNumber ?? string.Empty).Trim());

        var crewLeadByTruckId = await _db.Set<CrewEntity>()
            .AsNoTracking()
            .Where(c => c.WorkDate == workDate && c.TruckNumber != null)
            .ToListAsync();

        var leadTechnicianIdByTruckId = crewLeadByTruckId
            .Select(c => new
            {
                Crew = c,
                TruckNumber = (c.TruckNumber ?? string.Empty).Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.TruckNumber))
            .Join(
                truckNumberById,
                c => c.TruckNumber,
                t => t.Value,
                (c, t) => new
                {
                    TruckId = t.Key,
                    c.Crew.LeadTechnicianId
                },
                StringComparer.OrdinalIgnoreCase)
            .Where(x => x.LeadTechnicianId.HasValue)
            .GroupBy(x => x.TruckId)
            .ToDictionary(
                g => g.Key,
                g => (int?)g.First().LeadTechnicianId!.Value);

        return new TruckBoardDto
        {
            WorkDate = workDate,
            Unassigned = unassigned,
            AllTechnicians = allTechnicians,
            Trucks = trucks.Select(tr =>
            {
                var technicians = techsByTruck.TryGetValue(tr.Id, out var list)
                    ? list
                    : new List<TechnicianDto>();

                leadTechnicianIdByTruckId.TryGetValue(tr.Id, out var leadTechnicianId);

                var leadTechnicianName = leadTechnicianId.HasValue
                    ? technicians.FirstOrDefault(t => t.Id == leadTechnicianId.Value)?.Name
                    : null;

                return new TruckColumnDto
                {
                    Truck = tr,
                    Technicians = technicians,
                    LeadTechnicianId = leadTechnicianId,
                    LeadTechnicianName = leadTechnicianName
                };
            }).ToList()
        };
    }

    [HttpPost("board/initialize")]
    public async Task<IActionResult> InitializeBoard([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

        await _truckBoardInitialization.EnsureBoardInitializedAsync(workDate);

        return NoContent();
    }

    [HttpPut("board/commit")]
    public async Task<IActionResult> CommitBoard([FromBody] CommitTruckBoardRequest req, CancellationToken ct)
    {
        var workDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;

        var submittedAssignments = (req.Assignments ?? new List<CommitTruckAssignmentDto>())
            .Where(x => x.TechnicianId > 0 && x.TruckId > 0)
            .GroupBy(x => x.TechnicianId)
            .Select(g => g.Last())
            .ToList();

        var leadOverrides = (req.LeadOverrides ?? new List<CommitTruckLeadOverrideDto>())
            .Where(x => x.TruckId > 0 && x.TechnicianId > 0)
            .GroupBy(x => x.TruckId)
            .Select(g => g.Last())
            .ToList();

        var technicianIds = submittedAssignments
            .Select(x => (uint)x.TechnicianId)
            .Distinct()
            .ToList();

        var truckIds = submittedAssignments
            .Select(x => (uint)x.TruckId)
            .Distinct()
            .ToList();

        if (technicianIds.Count > 0)
        {
            var validTechnicianIds = await ActiveFieldTechniciansQuery()
                .Where(t => technicianIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync();

            var validTechnicianIdSet = validTechnicianIds.ToHashSet();

            var invalidTechnicianIds = technicianIds
                .Where(id => !validTechnicianIdSet.Contains(id))
                .OrderBy(id => id)
                .ToList();

            if (invalidTechnicianIds.Count > 0)
            {
                return BadRequest(
                    "One or more technicians are inactive, missing, or do not have the Technician role: " +
                    string.Join(", ", invalidTechnicianIds));
            }
        }

        if (truckIds.Count > 0)
        {
            var validTruckIds = await _db.Set<TruckEntity>()
                .AsNoTracking()
                .Where(t => t.IsActive && truckIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync();

            var validTruckIdSet = validTruckIds.ToHashSet();

            var invalidTruckIds = truckIds
                .Where(id => !validTruckIdSet.Contains(id))
                .OrderBy(id => id)
                .ToList();

            if (invalidTruckIds.Count > 0)
            {
                return BadRequest(
                    "One or more trucks are inactive or missing: " +
                    string.Join(", ", invalidTruckIds));
            }
        }

        var submittedTechIdsByTruckId = submittedAssignments
            .GroupBy(x => x.TruckId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.TechnicianId).ToHashSet());

        foreach (var leadOverride in leadOverrides)
        {
            if (!submittedTechIdsByTruckId.TryGetValue(leadOverride.TruckId, out var techIdsInTruck))
                return BadRequest($"Lead override truck {leadOverride.TruckId} has no assigned technicians.");

            if (techIdsInTruck.Count < 2)
                return BadRequest($"Lead override truck {leadOverride.TruckId} must have two or more technicians.");

            if (!techIdsInTruck.Contains(leadOverride.TechnicianId))
                return BadRequest($"Lead technician {leadOverride.TechnicianId} is not assigned to truck {leadOverride.TruckId}.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        try
        {
            // Clear today's technician-to-crew rows first.
            var existingTechnicianRosterRows = await _db.Set<TechnicianRosterEntity>()
                .Where(r => r.WorkDate == workDate)
                .ToListAsync();

            if (existingTechnicianRosterRows.Count > 0)
                _db.Set<TechnicianRosterEntity>().RemoveRange(existingTechnicianRosterRows);

            // Clear today's truck roster rows.
            var existingTruckRosterRows = await _db.Set<TruckRosterEntity>()
                .Where(r => r.WorkDate == workDate)
                .ToListAsync();

            if (existingTruckRosterRows.Count > 0)
                _db.Set<TruckRosterEntity>().RemoveRange(existingTruckRosterRows);

            await _db.SaveChangesAsync();

            // Insert the submitted board.
            foreach (var assignment in submittedAssignments)
            {
                _db.Set<TruckRosterEntity>().Add(new TruckRosterEntity
                {
                    WorkDate = workDate,
                    TechnicianId = (uint)assignment.TechnicianId,
                    TruckId = (uint)assignment.TruckId
                });
            }

            await _db.SaveChangesAsync();

            // Rebuild crews only for affected/active trucks.
            var activeTruckIds = await _db.Set<TruckEntity>()
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => t.Id)
                .ToListAsync();

            foreach (var truckId in activeTruckIds)
                await SyncCrewForTruckAsync(workDate, truckId);

            // Apply manual lead overrides after crew sync.
            foreach (var leadOverride in leadOverrides)
            {
                var truckId = (uint)leadOverride.TruckId;
                var technicianId = (uint)leadOverride.TechnicianId;

                var truck = await _db.Set<TruckEntity>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == truckId && t.IsActive);

                if (truck == null)
                    continue;

                var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(truckNumber))
                    continue;

                var crew = await _db.Set<CrewEntity>()
                    .Where(c => c.WorkDate == workDate && c.TruckNumber == truckNumber)
                    .OrderBy(c => c.Id)
                    .FirstOrDefaultAsync();

                if (crew == null)
                    continue;

                crew.LeadTechnicianId = technicianId;
            }

            await _truckBoardInitialization.MarkExplicitSaveAsync(workDate);

            await _db.SaveChangesAsync();

            await ReconcilePublishedCrewTicketAssignedTechAsync(
                workDate,
                ct);

            await tx.CommitAsync(ct);

            return NoContent();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpPut("board/move")]
    public async Task<IActionResult> Move([FromBody] MoveTechnicianRequest req)
    {
        var workDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;
        var techId = (uint)req.TechnicianId;
        uint? toTruckId = req.ToTruckId == null ? null : (uint)req.ToTruckId.Value;

        var techExists = await ActiveFieldTechniciansQuery()
            .AnyAsync(t => t.Id == techId);

        if (!techExists)
            return NotFound($"Technician {req.TechnicianId} was not found, is inactive, or does not have the Technician role.");

        if (toTruckId != null)
        {
            var truckOk = await _db.Set<TruckEntity>()
                .AsNoTracking()
                .AnyAsync(t => t.Id == toTruckId && t.IsActive);

            if (!truckOk)
                return NotFound($"Truck {req.ToTruckId} not found (or not active).");
        }

        var existing = await _db.Set<TruckRosterEntity>()
            .FirstOrDefaultAsync(r => r.WorkDate == workDate && r.TechnicianId == techId);

        var fromTruckId = existing?.TruckId;

        if (toTruckId == null)
        {
            if (existing != null)
                _db.Set<TruckRosterEntity>().Remove(existing);
        }
        else
        {
            if (existing == null)
            {
                _db.Set<TruckRosterEntity>().Add(new TruckRosterEntity
                {
                    WorkDate = workDate,
                    TechnicianId = techId,
                    TruckId = toTruckId.Value
                });
            }
            else
            {
                existing.TruckId = toTruckId.Value;
            }
        }

        await _db.SaveChangesAsync();

        if (fromTruckId != null)
            await SyncCrewForTruckAsync(workDate, fromTruckId.Value);

        if (toTruckId != null && toTruckId != fromTruckId)
            await SyncCrewForTruckAsync(workDate, toTruckId.Value);

        return NoContent();
    }

    [HttpPut("board/set-lead")]
    public async Task<IActionResult> SetCrewLead([FromBody] SetTruckCrewLeadRequest req)
    {
        var workDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;

        if (req.TruckId <= 0)
            return BadRequest("TruckId is required.");

        if (req.TechnicianId <= 0)
            return BadRequest("TechnicianId is required.");

        var truckId = (uint)req.TruckId;
        var technicianId = (uint)req.TechnicianId;

        var truck = await _db.Set<TruckEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == truckId && t.IsActive);

        if (truck == null)
            return NotFound($"Truck {req.TruckId} was not found or is inactive.");

        var technician = await ActiveFieldTechniciansQuery()
            .FirstOrDefaultAsync(t => t.Id == technicianId);

        if (technician == null)
            return NotFound($"Technician {req.TechnicianId} was not found, is inactive, or does not have the Technician role.");

        var rosterRows = await _db.Set<TruckRosterEntity>()
            .Where(r => r.WorkDate == workDate && r.TruckId == truckId)
            .ToListAsync();

        if (rosterRows.Count < 2)
            return BadRequest("A crew lead can only be set when two or more technicians are assigned to the truck.");

        var technicianIsInTruck = rosterRows.Any(r => r.TechnicianId == technicianId);

        if (!technicianIsInTruck)
            return BadRequest("The selected technician is not assigned to this truck on the selected date.");

        var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(truckNumber))
            return BadRequest("The selected truck does not have a valid truck number.");

        var crew = await _db.Set<CrewEntity>()
            .Where(c => c.WorkDate == workDate && c.TruckNumber == truckNumber)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();

        if (crew == null)
        {
            crew = new CrewEntity
            {
                WorkDate = workDate,
                TruckNumber = truckNumber,
                LeadTechnicianId = technicianId
            };

            _db.Set<CrewEntity>().Add(crew);
            await _db.SaveChangesAsync();
        }
        else
        {
            crew.LeadTechnicianId = technicianId;
            await _db.SaveChangesAsync();
        }

        var duplicateCrews = await _db.Set<CrewEntity>()
            .Where(c => c.WorkDate == workDate && c.TruckNumber == truckNumber && c.Id != crew.Id)
            .ToListAsync();

        if (duplicateCrews.Count > 0)
            _db.Set<CrewEntity>().RemoveRange(duplicateCrews);

        var memberIds = rosterRows
            .Select(r => r.TechnicianId)
            .Distinct()
            .ToList();

        var existingForMembers = await _db.Set<TechnicianRosterEntity>()
            .Where(r => r.WorkDate == workDate && memberIds.Contains(r.TechnicianId))
            .ToListAsync();

        foreach (var memberId in memberIds)
        {
            var existing = existingForMembers.FirstOrDefault(r => r.TechnicianId == memberId);

            if (existing == null)
            {
                _db.Set<TechnicianRosterEntity>().Add(new TechnicianRosterEntity
                {
                    WorkDate = workDate,
                    TechnicianId = memberId,
                    CrewId = crew.Id
                });
            }
            else if (existing.CrewId != crew.Id)
            {
                existing.CrewId = crew.Id;
            }
        }

        var extraRows = await _db.Set<TechnicianRosterEntity>()
            .Where(r => r.WorkDate == workDate && r.CrewId == crew.Id && !memberIds.Contains(r.TechnicianId))
            .ToListAsync();

        if (extraRows.Count > 0)
            _db.Set<TechnicianRosterEntity>().RemoveRange(extraRows);

        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("board/clear")]
    public async Task<IActionResult> ClearBoard([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

        // Remove technician-to-crew rows first.
        var technicianRosterRows = await _db.Set<TechnicianRosterEntity>()
            .Where(r => r.WorkDate == workDate)
            .ToListAsync();

        if (technicianRosterRows.Count > 0)
            _db.Set<TechnicianRosterEntity>().RemoveRange(technicianRosterRows);

        // Clear tickets that may still point at today's crews.
        var crews = await _db.Set<CrewEntity>()
            .Where(c => c.WorkDate == workDate)
            .ToListAsync();

        var crewIds = crews.Select(c => c.Id).ToList();

        if (crewIds.Count > 0)
        {
            var ticketsUsingCrews = await _db.Set<TicketEntity>()
                .Where(t => t.AssignedCrewId != null && crewIds.Contains(t.AssignedCrewId.Value))
                .ToListAsync();

            foreach (var ticket in ticketsUsingCrews)
                ticket.AssignedCrewId = null;

            var draftAssignmentsUsingCrews = await _db.Set<DailyTicketAssignmentEntity>()
                .Where(a => a.CrewId != null && crewIds.Contains(a.CrewId.Value))
                .ToListAsync();

            foreach (var assignment in draftAssignmentsUsingCrews)
                assignment.CrewId = null;

            var publishedAssignmentsUsingCrews = await _db.Set<DailyTicketAssignmentPublishedEntity>()
                .Where(a => a.CrewId != null && crewIds.Contains(a.CrewId.Value))
                .ToListAsync();

            foreach (var assignment in publishedAssignmentsUsingCrews)
                assignment.CrewId = null;
        }

        if (crews.Count > 0)
            _db.Set<CrewEntity>().RemoveRange(crews);

        // Remove truck board assignments last.
        var truckRosterRows = await _db.Set<TruckRosterEntity>()
            .Where(r => r.WorkDate == workDate)
            .ToListAsync();

        if (truckRosterRows.Count > 0)
            _db.Set<TruckRosterEntity>().RemoveRange(truckRosterRows);

        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("board/set-home")]
    public async Task<IActionResult> SetHomeBoard([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

        // Clear existing board safely before applying home trucks.
        var existingTechnicianRoster = await _db.Set<TechnicianRosterEntity>()
            .Where(r => r.WorkDate == workDate)
            .ToListAsync();

        if (existingTechnicianRoster.Count > 0)
            _db.Set<TechnicianRosterEntity>().RemoveRange(existingTechnicianRoster);

        var existingCrews = await _db.Set<CrewEntity>()
            .Where(c => c.WorkDate == workDate)
            .ToListAsync();

        var existingCrewIds = existingCrews.Select(c => c.Id).ToList();

        if (existingCrewIds.Count > 0)
        {
            var ticketsUsingCrews = await _db.Set<TicketEntity>()
                .Where(t => t.AssignedCrewId != null && existingCrewIds.Contains(t.AssignedCrewId.Value))
                .ToListAsync();

            foreach (var ticket in ticketsUsingCrews)
                ticket.AssignedCrewId = null;

            var draftAssignmentsUsingCrews = await _db.Set<DailyTicketAssignmentEntity>()
                .Where(a => a.CrewId != null && existingCrewIds.Contains(a.CrewId.Value))
                .ToListAsync();

            foreach (var assignment in draftAssignmentsUsingCrews)
                assignment.CrewId = null;

            var publishedAssignmentsUsingCrews = await _db.Set<DailyTicketAssignmentPublishedEntity>()
                .Where(a => a.CrewId != null && existingCrewIds.Contains(a.CrewId.Value))
                .ToListAsync();

            foreach (var assignment in publishedAssignmentsUsingCrews)
                assignment.CrewId = null;
        }

        if (existingCrews.Count > 0)
            _db.Set<CrewEntity>().RemoveRange(existingCrews);

        var existingTruckRoster = await _db.Set<TruckRosterEntity>()
            .Where(r => r.WorkDate == workDate)
            .ToListAsync();

        if (existingTruckRoster.Count > 0)
            _db.Set<TruckRosterEntity>().RemoveRange(existingTruckRoster);

        await _db.SaveChangesAsync();

        var activeTruckIds = await _db.Set<TruckEntity>()
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync();

        var activeTruckIdSet = activeTruckIds.ToHashSet();

        var techsWithHomeTruck = await ActiveFieldTechniciansQuery()
            .Where(t => t.HomeTruckId != null)
            .ToListAsync();

        foreach (var tech in techsWithHomeTruck)
        {
            if (tech.HomeTruckId == null)
                continue;

            if (!activeTruckIdSet.Contains(tech.HomeTruckId.Value))
                continue;

            _db.Set<TruckRosterEntity>().Add(new TruckRosterEntity
            {
                WorkDate = workDate,
                TechnicianId = tech.Id,
                TruckId = tech.HomeTruckId.Value
            });
        }

        await _db.SaveChangesAsync();

        var affectedTruckIds = techsWithHomeTruck
            .Where(t => t.HomeTruckId != null && activeTruckIdSet.Contains(t.HomeTruckId.Value))
            .Select(t => t.HomeTruckId!.Value)
            .Distinct()
            .ToList();

        foreach (var truckId in affectedTruckIds)
            await SyncCrewForTruckAsync(workDate, truckId);

        return NoContent();
    }

    [HttpPost("board/cleanup-invalid-roster")]
    public async Task<ActionResult<string>> CleanupInvalidRosterRows([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

        var validTechnicianIds = await ActiveFieldTechniciansQuery()
            .Select(t => t.Id)
            .ToListAsync();

        var validTechnicianIdSet = validTechnicianIds.ToHashSet();

        var invalidTruckRosterRows = await _db.Set<TruckRosterEntity>()
            .Where(r =>
                r.WorkDate == workDate &&
                !validTechnicianIdSet.Contains(r.TechnicianId))
            .ToListAsync();

        var invalidTechnicianRosterRows = await _db.Set<TechnicianRosterEntity>()
            .Where(r =>
                r.WorkDate == workDate &&
                !validTechnicianIdSet.Contains(r.TechnicianId))
            .ToListAsync();

        var affectedTruckIds = invalidTruckRosterRows
            .Select(r => r.TruckId)
            .Distinct()
            .ToList();

        var affectedCrewIds = invalidTechnicianRosterRows
            .Select(r => r.CrewId)
            .Distinct()
            .ToList();

        if (invalidTechnicianRosterRows.Count > 0)
            _db.Set<TechnicianRosterEntity>().RemoveRange(invalidTechnicianRosterRows);

        if (invalidTruckRosterRows.Count > 0)
            _db.Set<TruckRosterEntity>().RemoveRange(invalidTruckRosterRows);

        await _db.SaveChangesAsync();

        foreach (var truckId in affectedTruckIds)
            await SyncCrewForTruckAsync(workDate, truckId);

        var emptyCrews = await _db.Set<CrewEntity>()
            .Where(c =>
                c.WorkDate == workDate &&
                !_db.Set<TechnicianRosterEntity>().Any(r => r.WorkDate == workDate && r.CrewId == c.Id))
            .ToListAsync();

        if (emptyCrews.Count > 0)
        {
            var emptyCrewIds = emptyCrews.Select(c => c.Id).ToList();

            var ticketsUsingEmptyCrews = await _db.Set<TicketEntity>()
                .Where(t => t.AssignedCrewId != null && emptyCrewIds.Contains(t.AssignedCrewId.Value))
                .ToListAsync();

            foreach (var ticket in ticketsUsingEmptyCrews)
                ticket.AssignedCrewId = null;

            _db.Set<CrewEntity>().RemoveRange(emptyCrews);

            await _db.SaveChangesAsync();
        }

        return Ok(
            $"Cleaned {invalidTruckRosterRows.Count} truck roster row(s), " +
            $"{invalidTechnicianRosterRows.Count} technician roster row(s), " +
            $"{emptyCrews.Count} empty crew row(s).");
    }

    private IQueryable<TechnicianEntity> ActiveFieldTechniciansQuery()
    {
        return _db.Set<TechnicianEntity>()
            .AsNoTracking()
            .Where(t =>
                t.IsActive &&
                t.TechnicianRoles.Any(tr => tr.Role.Code == TechnicianRoleCode));
    }

    private static DateTime ParseDateOrToday(string? date)
        => (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
            ? parsed.Date
            : DateTime.Today.Date;

    private async Task SyncCrewForTruckAsync(DateTime workDate, uint truckId)
    {
        var truck = await _db.Set<TruckEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == truckId);

        if (truck == null)
            return;

        var truckNumber = truck.TruckNumber;

        var memberTechsUnordered = await (
            from roster in _db.Set<TruckRosterEntity>().AsNoTracking()
            join tech in ActiveFieldTechniciansQuery()
                on roster.TechnicianId equals tech.Id
            where roster.WorkDate == workDate && roster.TruckId == truckId
            select tech)
            .ToListAsync();

        var memberTechs = memberTechsUnordered
            .OrderByDescending(t => t.HomeTruckId == truckId)
            .ThenByDescending(GetTitleRank)
            .ThenBy(t => t.LastName)
            .ThenBy(t => t.FirstName)
            .ToList();

        var memberIds = memberTechs
            .Select(t => t.Id)
            .Distinct()
            .ToList();

        var crews = await _db.Set<CrewEntity>()
            .Where(c => c.WorkDate == workDate && c.TruckNumber == truckNumber)
            .ToListAsync();

        if (memberIds.Count <= 1)
        {
            if (crews.Count > 0)
            {
                var crewIds = crews.Select(c => c.Id).ToList();

                var technicianRosterRows = await _db.Set<TechnicianRosterEntity>()
                    .Where(r => r.WorkDate == workDate && crewIds.Contains(r.CrewId))
                    .ToListAsync();

                if (technicianRosterRows.Count > 0)
                    _db.Set<TechnicianRosterEntity>().RemoveRange(technicianRosterRows);

                var ticketsUsingCrews = await _db.Set<TicketEntity>()
                    .Where(t => t.AssignedCrewId != null && crewIds.Contains(t.AssignedCrewId.Value))
                    .ToListAsync();

                foreach (var ticket in ticketsUsingCrews)
                    ticket.AssignedCrewId = null;

                var draftAssignmentsUsingCrews = await _db.Set<DailyTicketAssignmentEntity>()
                    .Where(a => a.CrewId != null && crewIds.Contains(a.CrewId.Value))
                    .ToListAsync();

                foreach (var assignment in draftAssignmentsUsingCrews)
                    assignment.CrewId = null;

                var publishedAssignmentsUsingCrews = await _db.Set<DailyTicketAssignmentPublishedEntity>()
                    .Where(a => a.CrewId != null && crewIds.Contains(a.CrewId.Value))
                    .ToListAsync();

                foreach (var assignment in publishedAssignmentsUsingCrews)
                    assignment.CrewId = null;

                _db.Set<CrewEntity>().RemoveRange(crews);

                await _db.SaveChangesAsync();
            }

            return;
        }

        CrewEntity crew;

        if (crews.Count == 0)
        {
            var leadTech = PickLeadTechnician(memberTechs, truckId, null);

            if (leadTech == null)
                return;

            crew = new CrewEntity
            {
                WorkDate = workDate,
                TruckNumber = truckNumber,
                LeadTechnicianId = leadTech.Id
            };

            _db.Set<CrewEntity>().Add(crew);
            await _db.SaveChangesAsync();
        }
        else
        {
            crew = crews[0];

            if (crews.Count > 1)
            {
                _db.Set<CrewEntity>().RemoveRange(crews.Skip(1));
                await _db.SaveChangesAsync();
            }

            var leadTech = PickLeadTechnician(
                memberTechs,
                truckId,
                crew.LeadTechnicianId);

            if (leadTech == null)
                return;

            if (crew.LeadTechnicianId != leadTech.Id)
            {
                crew.LeadTechnicianId = leadTech.Id;
                await _db.SaveChangesAsync();
            }
        }

        var existingForMembers = await _db.Set<TechnicianRosterEntity>()
            .Where(r => r.WorkDate == workDate && memberIds.Contains(r.TechnicianId))
            .ToListAsync();

        foreach (var techId in memberIds)
        {
            var r = existingForMembers.FirstOrDefault(x => x.TechnicianId == techId);

            if (r == null)
            {
                _db.Set<TechnicianRosterEntity>().Add(new TechnicianRosterEntity
                {
                    WorkDate = workDate,
                    TechnicianId = techId,
                    CrewId = crew.Id
                });
            }
            else if (r.CrewId != crew.Id)
            {
                r.CrewId = crew.Id;
            }
        }

        var extra = await _db.Set<TechnicianRosterEntity>()
            .Where(r => r.WorkDate == workDate && r.CrewId == crew.Id && !memberIds.Contains(r.TechnicianId))
            .ToListAsync();

        if (extra.Count > 0)
            _db.Set<TechnicianRosterEntity>().RemoveRange(extra);

        await _db.SaveChangesAsync();
    }

    private static TechnicianEntity? PickLeadTechnician(IReadOnlyList<TechnicianEntity> technicians, uint truckId, uint? currentLeadTechnicianId)
    {
        if (technicians.Count == 0)
            return null;

        if (currentLeadTechnicianId.HasValue)
        {
            var currentLead = technicians.FirstOrDefault(t => t.Id == currentLeadTechnicianId.Value);

            if (currentLead != null)
                return currentLead;
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

    private async Task ReconcilePublishedCrewTicketAssignedTechAsync(DateTime rosterDate, CancellationToken ct)
    {
        var assignmentDate = new DateTime(2000, 1, 1);

        /*
         * When truck membership changes, published crew-route tickets need their
         * AssignedTech display recalculated. Otherwise a removed non-lead technician
         * can keep seeing old crew tickets under Field Tech > Other Assigned Tickets.
         */

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

        var protectedStatusNames = statusRows
            .Where(x => x.IsClosed || x.IsFieldComplete)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allPublishedRows = await _db.Set<DailyTicketAssignmentPublishedEntity>()
            .AsNoTracking()
            .Where(x =>
                x.AssignmentDate == assignmentDate &&
                x.TargetType == "Technician" &&
                x.TechnicianId.HasValue)
            .ToListAsync(ct);

        var currentCrewPublishedRows = allPublishedRows
            .GroupBy(x => x.TechnicianId!.Value)
            .SelectMany(g =>
            {
                var latestVersion = g.Max(x => x.PublishedVersion);

                return g.Where(x =>
                    x.PublishedVersion == latestVersion &&
                    x.TruckId.HasValue);
            })
            .ToList();

        if (currentCrewPublishedRows.Count == 0)
            return;

        var truckIds = currentCrewPublishedRows
            .Select(x => x.TruckId!.Value)
            .Distinct()
            .ToList();

        var ownerTechnicianIds = currentCrewPublishedRows
            .Select(x => x.TechnicianId!.Value)
            .Distinct()
            .ToList();

        var ticketIds = currentCrewPublishedRows
            .Select(x => x.TicketId)
            .Distinct()
            .ToList();

        var rosterRows = await (
            from roster in _db.Set<TruckRosterEntity>().AsNoTracking()
            join tech in ActiveFieldTechniciansQuery()
                on roster.TechnicianId equals tech.Id
            where roster.WorkDate == rosterDate.Date &&
                  truckIds.Contains(roster.TruckId)
            select new
            {
                roster.TruckId,
                tech.Id,
                tech.EmployeeId,
                tech.FirstName,
                tech.LastName
            })
            .ToListAsync(ct);

        var assignedTextByTruckId = rosterRows
            .GroupBy(x => x.TruckId)
            .ToDictionary(
                g => g.Key,
                g => FormatCrewDisplayText(
                    g.Select(x => FormatTechnicianName(
                            x.FirstName,
                            x.LastName,
                            x.EmployeeId))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList()));

        var ownerTechsById = await ActiveFieldTechniciansQuery()
            .Where(x => ownerTechnicianIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => FormatTechnicianName(
                    x.FirstName,
                    x.LastName,
                    x.EmployeeId),
                ct);

        var ticketsById = await _db.Set<TicketEntity>()
            .Where(x => ticketIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var changed = false;

        foreach (var row in currentCrewPublishedRows)
        {
            if (!ticketsById.TryGetValue(row.TicketId, out var ticket))
                continue;

            if (protectedStatusNames.Contains(ticket.Status ?? string.Empty))
                continue;

            var newAssignedTech = "";

            if (row.TruckId.HasValue &&
                assignedTextByTruckId.TryGetValue(row.TruckId.Value, out var crewText) &&
                !string.IsNullOrWhiteSpace(crewText))
            {
                newAssignedTech = crewText;
            }
            else if (row.TechnicianId.HasValue &&
                     ownerTechsById.TryGetValue(row.TechnicianId.Value, out var ownerName))
            {
                newAssignedTech = ownerName;
            }

            if (string.IsNullOrWhiteSpace(newAssignedTech))
                continue;

            if (string.Equals(
                    ticket.AssignedTech ?? "",
                    newAssignedTech,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ticket.AssignedTech = newAssignedTech;

            /*
             * Do not touch LastActivityAt here. This is roster-display cleanup,
             * not a ticket activity event.
             */
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync(ct);
    }

    private static string FormatTechnicianName(string? firstName, string? lastName, string? fallbackEmployeeId)
    {
        var fullName =
            $"{firstName ?? string.Empty} {lastName ?? string.Empty}".Trim();

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return (fallbackEmployeeId ?? "Unknown").Trim();
    }

    private static string FormatCrewDisplayText(IReadOnlyList<string> names)
    {
        var cleanNames = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanNames.Count == 0)
            return "";

        if (cleanNames.Count == 1)
            return cleanNames[0];

        if (cleanNames.Count == 2)
            return $"{cleanNames[0]} & {cleanNames[1]}";

        return string.Join(", ", cleanNames.Take(cleanNames.Count - 1)) +
               " & " +
               cleanNames.Last();
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
}