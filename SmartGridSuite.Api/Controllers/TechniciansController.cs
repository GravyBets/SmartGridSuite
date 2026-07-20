#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Administration.Technicians;
using SmartGridSuite.Contracts.Crews;
using System.Net.Mail;

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

                EmailAddress = t.EmailAddress,

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
        var emailAddress = NormalizeEmailAddress(req.EmailAddress);
        var roleCodes = NormalizeRoleCodes(req.RoleCodes);

        if (employeeId.Length == 0)
            return BadRequest("EmployeeId is required.");

        if (firstName.Length == 0)
            return BadRequest("FirstName is required.");

        if (lastName.Length == 0)
            return BadRequest("LastName is required.");

        if (title == null)
            return BadRequest("Title must be one of: Apprentice, Journeyman, Head Journeyman, Supervisor.");

        if (!string.IsNullOrWhiteSpace(emailAddress) && !IsValidEmailAddress(emailAddress))
        {
            return BadRequest("Email address is invalid.");
        }

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
            EmailAddress = emailAddress,
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
        var emailAddress = NormalizeEmailAddress(req.EmailAddress);
        var roleCodes = NormalizeRoleCodes(req.RoleCodes);

        if (employeeId.Length == 0)
            return BadRequest("EmployeeId is required.");

        if (firstName.Length == 0)
            return BadRequest("FirstName is required.");

        if (lastName.Length == 0)
            return BadRequest("LastName is required.");

        if (title == null)
            return BadRequest("Title must be one of: Apprentice, Journeyman, Head Journeyman, Supervisor.");

        if (!string.IsNullOrWhiteSpace(emailAddress) && !IsValidEmailAddress(emailAddress))
        {
            return BadRequest("Email address is invalid.");
        }

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
        entity.EmailAddress = emailAddress;
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

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var entity = await _db.Technicians
            .FirstOrDefaultAsync(t => t.Id == (uint)id);

        if (entity == null)
            return NotFound();

        var existingRoles = await _db.TechnicianRoles
            .Where(x => x.TechnicianId == entity.Id)
            .ToListAsync();

        _db.TechnicianRoles.RemoveRange(existingRoles);

        var workdayOverrides = await _db.TechnicianWorkdayOverrides
            .Where(x => x.TechnicianId == entity.Id)
            .ToListAsync();

        _db.TechnicianWorkdayOverrides.RemoveRange(workdayOverrides);

        _db.Technicians.Remove(entity);

        try
        {
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (DbUpdateException)
        {
            return Conflict(
                "This technician cannot be deleted because related records exist. " +
                "Set Active Employee to No instead.");
        }
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

    private static string? NormalizeEmailAddress(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text;
    }

    private static bool IsValidEmailAddress(string value)
    {
        try
        {
            _ = new MailAddress(value.Trim());
            return true;
        }
        catch
        {
            return false;
        }
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

    [HttpGet("by-employee-id/{employeeId}")]
    public async Task<ActionResult<TechnicianDto>> GetByEmployeeId(string employeeId, CancellationToken ct)
    {
        employeeId = (employeeId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(employeeId))
            return BadRequest("Employee ID is required.");

        var workDate = DateTime.Today.Date;

        var tech = await _db.Technicians
            .AsNoTracking()
            .Include(t => t.HomeTruck)
            .Include(t => t.TechnicianRoles)
                .ThenInclude(tr => tr.Role)
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive, ct);

        if (tech is null)
            return NotFound();

        var overrideValue = await _db.TechnicianWorkdayOverrides
            .AsNoTracking()
            .Where(x => x.WorkDate == workDate && x.TechnicianId == tech.Id)
            .Select(x => (bool?)x.IsWorking)
            .FirstOrDefaultAsync(ct);

        var effectiveIsWorking = overrideValue
            ?? GetDefaultWorkingStatus(tech, workDate.DayOfWeek);

        var fullName = $"{tech.FirstName} {tech.LastName}".Trim();

        return Ok(new TechnicianDto
        {
            Id = (int)tech.Id,
            EmployeeId = tech.EmployeeId,

            FirstName = tech.FirstName,
            LastName = tech.LastName,
            Name = fullName,
            Title = tech.Title,
            EmailAddress = tech.EmailAddress,

            IsActive = tech.IsActive,

            HomeTruckId = tech.HomeTruckId == null ? null : (int?)tech.HomeTruckId.Value,
            HomeTruckNumber = tech.HomeTruck?.TruckNumber,
            HomeTruckDisplayName = tech.HomeTruck?.DisplayName,

            WorksMonday = tech.WorksMonday,
            WorksTuesday = tech.WorksTuesday,
            WorksWednesday = tech.WorksWednesday,
            WorksThursday = tech.WorksThursday,
            WorksFriday = tech.WorksFriday,
            WorksSaturday = tech.WorksSaturday,
            WorksSunday = tech.WorksSunday,

            RoleCodes = tech.TechnicianRoles
                .Select(x => x.Role.Code)
                .OrderBy(x => x)
                .ToList(),

            IsOnShift = effectiveIsWorking,
            TruckNumber = tech.HomeTruck?.TruckNumber
        });
    }

    [HttpGet("current-crew/{employeeId}")]
    public async Task<ActionResult<CurrentCrewDto>> GetCurrentCrew(string employeeId, CancellationToken ct)
    {
        employeeId = (employeeId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return Ok(new CurrentCrewDto
            {
                PrimaryTech = "Unknown",
                DisplayText = "Unknown"
            });
        }

        var workDate = DateTime.Today;

        var submitter = await _db.Technicians
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive, ct);

        if (submitter is null)
        {
            return Ok(new CurrentCrewDto
            {
                PrimaryTech = employeeId,
                DisplayText = employeeId
            });
        }

        var primaryName = FormatTechnicianName(
            submitter.FirstName,
            submitter.LastName,
            submitter.EmployeeId);

        var submitterRoster = await _db.TruckRosters
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.WorkDate == workDate &&
                x.TechnicianId == submitter.Id,
                ct);

        if (submitterRoster is null)
        {
            return Ok(new CurrentCrewDto
            {
                PrimaryTech = primaryName,
                DisplayText = primaryName
            });
        }

        var crewTechs = await (
            from roster in _db.TruckRosters.AsNoTracking()
            join tech in _db.Technicians.AsNoTracking()
                on roster.TechnicianId equals tech.Id
            where roster.WorkDate == workDate
                  && roster.TruckId == submitterRoster.TruckId
                  && tech.IsActive
            select new
            {
                tech.Id,
                tech.EmployeeId,
                tech.FirstName,
                tech.LastName,
            })
            .ToListAsync(ct);

        var secondaryTechs = crewTechs
            .Where(x => x.Id != submitter.Id)
            .Select(x => FormatTechnicianName(
                x.FirstName,
                x.LastName,
                x.EmployeeId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var displayNames = new List<string> { primaryName };
        displayNames.AddRange(secondaryTechs);

        return Ok(new CurrentCrewDto
        {
            PrimaryTech = primaryName,
            SecondaryTechs = secondaryTechs,
            DisplayText = FormatCrewDisplayText(displayNames)
        });
    }

    private static string FormatTechnicianName(string? firstName, string? lastName, string? fallbackEmployeeId)
    {
        var fullName = $"{firstName ?? string.Empty} {lastName ?? string.Empty}".Trim();

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
            return "Unknown";

        if (cleanNames.Count == 1)
            return cleanNames[0];

        if (cleanNames.Count == 2)
            return $"{cleanNames[0]} & {cleanNames[1]}";

        return string.Join(", ", cleanNames.Take(cleanNames.Count - 1)) +
               " & " +
               cleanNames.Last();
    }
}