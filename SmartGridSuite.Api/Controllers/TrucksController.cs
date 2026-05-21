#nullable enable
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Administration.Technicians;
using SmartGridSuite.Contracts.Administration.Trucks;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TrucksController : ControllerBase
{
    private readonly SmartGridDbContext _db;

    public TrucksController(SmartGridDbContext db) => _db = db;

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

    [HttpGet("board")]
    public async Task<ActionResult<TruckBoardDto>> GetBoard([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

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
            .Join(_db.Set<TechnicianEntity>().AsNoTracking(),
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

        var unassigned = await _db.Set<TechnicianEntity>()
            .AsNoTracking()
            .Where(t => t.IsActive && !_db.Set<TruckRosterEntity>()
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

        var allTechnicians = await _db.Set<TechnicianEntity>()
            .AsNoTracking()
            .Where(t => t.IsActive)
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

        return new TruckBoardDto
        {
            WorkDate = workDate,
            Unassigned = unassigned,
            AllTechnicians = allTechnicians,
            Trucks = trucks.Select(tr => new TruckColumnDto
            {
                Truck = tr,
                Technicians = techsByTruck.TryGetValue(tr.Id, out var list)
                    ? list
                    : new List<TechnicianDto>()
            }).ToList()
        };
    }

    [HttpPost("board/initialize")]
    public async Task<IActionResult> InitializeBoard([FromQuery] string? date = null)
    {
        var workDate = ParseDateOrToday(date);

        var already = await _db.Set<TruckRosterEntity>()
            .AsNoTracking()
            .AnyAsync(r => r.WorkDate == workDate);

        if (already)
            return NoContent();

        var priorDate = await _db.Set<TruckRosterEntity>()
            .AsNoTracking()
            .Where(r => r.WorkDate < workDate)
            .OrderByDescending(r => r.WorkDate)
            .Select(r => r.WorkDate)
            .FirstOrDefaultAsync();

        if (priorDate == default)
            return NoContent();

        var priorRows = await _db.Set<TruckRosterEntity>()
            .AsNoTracking()
            .Where(r => r.WorkDate == priorDate)
            .ToListAsync();

        foreach (var r in priorRows)
        {
            _db.Set<TruckRosterEntity>().Add(new TruckRosterEntity
            {
                WorkDate = workDate,
                TruckId = r.TruckId,
                TechnicianId = r.TechnicianId
            });
        }

        await _db.SaveChangesAsync();

        var truckIds = priorRows.Select(x => x.TruckId).Distinct().ToList();
        foreach (var tid in truckIds)
            await SyncCrewForTruckAsync(workDate, tid);

        return NoContent();
    }

    [HttpPut("board/move")]
    public async Task<IActionResult> Move([FromBody] MoveTechnicianRequest req)
    {
        var workDate = (req.WorkDate == default ? DateTime.Today : req.WorkDate).Date;
        var techId = (uint)req.TechnicianId;
        uint? toTruckId = req.ToTruckId == null ? null : (uint)req.ToTruckId.Value;

        var techExists = await _db.Set<TechnicianEntity>()
            .AsNoTracking()
            .AnyAsync(t => t.Id == techId);

        if (!techExists)
            return NotFound($"Technician {req.TechnicianId} not found.");

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

        var techsWithHomeTruck = await _db.Set<TechnicianEntity>()
            .AsNoTracking()
            .Where(t => t.IsActive && t.HomeTruckId != null)
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

    private static DateTime ParseDateOrToday(string? date)
        => (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
            ? parsed.Date
            : DateTime.Today.Date;

    private async Task SyncCrewForTruckAsync(DateTime workDate, uint truckId)
    {
        var truck = await _db.Set<TruckEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == truckId);

        if (truck == null) return;

        var truckNumber = truck.TruckNumber;

        var memberIds = await _db.Set<TruckRosterEntity>()
            .AsNoTracking()
            .Where(r => r.WorkDate == workDate && r.TruckId == truckId)
            .Select(r => r.TechnicianId)
            .ToListAsync();

        var crews = await _db.Set<CrewEntity>()
            .Where(c => c.WorkDate == workDate && c.TruckNumber == truckNumber)
            .ToListAsync();

        if (memberIds.Count <= 1)
        {
            if (crews.Count > 0)
            {
                _db.Set<CrewEntity>().RemoveRange(crews);
                await _db.SaveChangesAsync();
            }
            return;
        }

        CrewEntity crew;
        if (crews.Count == 0)
        {
            crew = new CrewEntity
            {
                WorkDate = workDate,
                TruckNumber = truckNumber,
                LeadTechnicianId = memberIds[0]
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

            if (crew.LeadTechnicianId == null || !memberIds.Contains(crew.LeadTechnicianId.Value))
            {
                crew.LeadTechnicianId = memberIds[0];
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