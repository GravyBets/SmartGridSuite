#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Administration.Technicians;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TechniciansController : ControllerBase
{
    private readonly SmartGridDbContext _db;

    public TechniciansController(SmartGridDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<TechnicianDto>>> Get(
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? workDate = null)
    {
        var date = ParseDateOrToday(workDate);

        var q = _db.Technicians
            .AsNoTracking()
            .Include(t => t.HomeTruck)
            .Include(t => t.TechnicianRoles)
                .ThenInclude(tr => tr.Role)
            .AsQueryable();

        if (!includeInactive)
            q = q.Where(t => t.IsActive);

        var techs = await q
            .OrderBy(t => t.LastName)
            .ThenBy(t => t.FirstName)
            .ToListAsync();

        var techIds = techs.Select(t => t.Id).ToList();

        var overrides = await _db.TechnicianWorkdayOverrides
            .AsNoTracking()
            .Where(x => x.WorkDate == date && techIds.Contains(x.TechnicianId))
            .ToDictionaryAsync(x => x.TechnicianId, x => x.IsWorking);

        var items = techs.Select(t =>
        {
            var effectiveIsWorking = overrides.TryGetValue(t.Id, out var overrideValue)
                ? overrideValue
                : GetDefaultWorkingStatus(t, date.DayOfWeek);

            var fullName = $"{t.FirstName} {t.LastName}".Trim();

            return new TechnicianDto
            {
                Id = (int)t.Id,
                EmployeeId = t.EmployeeId,

                FirstName = t.FirstName,
                LastName = t.LastName,
                Name = fullName,
                Title = t.Title,

                IsActive = t.IsActive,

                HomeTruckId = t.HomeTruckId == null ? null : (int?)t.HomeTruckId.Value,
                HomeTruckNumber = t.HomeTruck?.TruckNumber,
                HomeTruckDisplayName = t.HomeTruck?.DisplayName,

                WorksMonday = t.WorksMonday,
                WorksTuesday = t.WorksTuesday,
                WorksWednesday = t.WorksWednesday,
                WorksThursday = t.WorksThursday,
                WorksFriday = t.WorksFriday,
                WorksSaturday = t.WorksSaturday,
                WorksSunday = t.WorksSunday,

                RoleCodes = t.TechnicianRoles
                    .Select(x => x.Role.Code)
                    .OrderBy(x => x)
                    .ToList(),

                IsOnShift = effectiveIsWorking,
                TruckNumber = t.HomeTruck?.TruckNumber
            };
        }).ToList();

        return items;
    }

    [HttpPost]
    public async Task<ActionResult<CreateTechnicianResponse>> Create([FromBody] CreateTechnicianRequest req)
    {
        var employeeId = (req.EmployeeId ?? "").Trim();
        var firstName = (req.FirstName ?? "").Trim();
        var lastName = (req.LastName ?? "").Trim();
        var title = NormalizeTitle(req.Title);
        var roleCodes = NormalizeRoleCodes(req.RoleCodes);

        if (employeeId.Length == 0)
            return BadRequest("EmployeeId is required.");

        if (firstName.Length == 0)
            return BadRequest("FirstName is required.");

        if (lastName.Length == 0)
            return BadRequest("LastName is required.");

        if (title == null)
            return BadRequest("Title must be one of: Apprentice, Journeyman, Head Journeyman, Supervisor.");

        if (roleCodes.Count == 0)
            return BadRequest("At least one role is required.");

        var exists = await _db.Technicians
            .AsNoTracking()
            .AnyAsync(t => t.EmployeeId == employeeId);

        if (exists)
            return Conflict($"EmployeeId '{employeeId}' already exists.");

        uint? homeTruckId = req.HomeTruckId.HasValue ? (uint?)req.HomeTruckId.Value : null;

        if (homeTruckId.HasValue)
        {
            var homeTruckExists = await _db.Trucks
                .AsNoTracking()
                .AnyAsync(t => t.Id == homeTruckId.Value && t.IsActive);

            if (!homeTruckExists)
                return BadRequest($"HomeTruckId '{req.HomeTruckId}' was not found or is inactive.");
        }

        var roles = await _db.Roles
            .Where(r => roleCodes.Contains(r.Code))
            .ToListAsync();

        if (roles.Count != roleCodes.Count)
            return BadRequest("One or more role codes are invalid.");

        var entity = new TechnicianEntity
        {
            EmployeeId = employeeId,
            FirstName = firstName,
            LastName = lastName,
            Title = title,
            IsActive = req.IsActive,

            HomeTruckId = homeTruckId,

            WorksMonday = req.WorksMonday,
            WorksTuesday = req.WorksTuesday,
            WorksWednesday = req.WorksWednesday,
            WorksThursday = req.WorksThursday,
            WorksFriday = req.WorksFriday,
            WorksSaturday = req.WorksSaturday,
            WorksSunday = req.WorksSunday
        };

        _db.Technicians.Add(entity);
        await _db.SaveChangesAsync();

        foreach (var role in roles)
        {
            _db.TechnicianRoles.Add(new TechnicianRoleEntity
            {
                TechnicianId = entity.Id,
                RoleId = role.Id
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new CreateTechnicianResponse
        {
            Id = (long)entity.Id
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpdateTechnicianRequest req)
    {
        var entity = await _db.Technicians.FirstOrDefaultAsync(t => t.Id == (uint)id);
        if (entity == null)
            return NotFound();

        var employeeId = (req.EmployeeId ?? "").Trim();
        var firstName = (req.FirstName ?? "").Trim();
        var lastName = (req.LastName ?? "").Trim();
        var title = NormalizeTitle(req.Title);
        var roleCodes = NormalizeRoleCodes(req.RoleCodes);

        if (employeeId.Length == 0)
            return BadRequest("EmployeeId is required.");

        if (firstName.Length == 0)
            return BadRequest("FirstName is required.");

        if (lastName.Length == 0)
            return BadRequest("LastName is required.");

        if (title == null)
            return BadRequest("Title must be one of: Apprentice, Journeyman, Head Journeyman, Supervisor.");

        if (roleCodes.Count == 0)
            return BadRequest("At least one role is required.");

        var employeeExists = await _db.Technicians
            .AsNoTracking()
            .AnyAsync(t => t.EmployeeId == employeeId && t.Id != entity.Id);

        if (employeeExists)
            return Conflict($"EmployeeId '{employeeId}' already exists.");

        uint? homeTruckId = req.HomeTruckId.HasValue ? (uint?)req.HomeTruckId.Value : null;

        if (homeTruckId.HasValue)
        {
            var homeTruckExists = await _db.Trucks
                .AsNoTracking()
                .AnyAsync(t => t.Id == homeTruckId.Value && t.IsActive);

            if (!homeTruckExists)
                return BadRequest($"HomeTruckId '{req.HomeTruckId}' was not found or is inactive.");
        }

        var roles = await _db.Roles
            .Where(r => roleCodes.Contains(r.Code))
            .ToListAsync();

        if (roles.Count != roleCodes.Count)
            return BadRequest("One or more role codes are invalid.");

        entity.EmployeeId = employeeId;
        entity.FirstName = firstName;
        entity.LastName = lastName;
        entity.Title = title;
        entity.IsActive = req.IsActive;

        entity.HomeTruckId = homeTruckId;

        entity.WorksMonday = req.WorksMonday;
        entity.WorksTuesday = req.WorksTuesday;
        entity.WorksWednesday = req.WorksWednesday;
        entity.WorksThursday = req.WorksThursday;
        entity.WorksFriday = req.WorksFriday;
        entity.WorksSaturday = req.WorksSaturday;
        entity.WorksSunday = req.WorksSunday;

        var existingRoles = await _db.TechnicianRoles
            .Where(x => x.TechnicianId == entity.Id)
            .ToListAsync();

        _db.TechnicianRoles.RemoveRange(existingRoles);

        foreach (var role in roles)
        {
            _db.TechnicianRoles.Add(new TechnicianRoleEntity
            {
                TechnicianId = entity.Id,
                RoleId = role.Id
            });
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static DateTime ParseDateOrToday(string? date)
        => (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
            ? parsed.Date
            : DateTime.Today.Date;

    private static List<string> NormalizeRoleCodes(IEnumerable<string>? roleCodes)
        => (roleCodes ?? Array.Empty<string>())
            .Select(x => (x ?? "").Trim().ToUpperInvariant())
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList();

    private static string? NormalizeTitle(string? title)
        => (title ?? "").Trim().ToUpperInvariant() switch
        {
            "APPRENTICE" => "Apprentice",
            "JOURNEYMAN" => "Journeyman",
            "HEAD JOURNEYMAN" => "Head Journeyman",
            "SUPERVISOR" => "Supervisor",
            _ => null
        };

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
}