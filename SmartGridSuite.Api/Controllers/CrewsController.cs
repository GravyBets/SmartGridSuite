#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Crews;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CrewsController : ControllerBase
{
    private readonly SmartGridDbContext _db;

    public CrewsController(SmartGridDbContext db) => _db = db;

    // GET /api/crews/today?date=2026-03-05   (date optional)
    [HttpGet("today")]
    public async Task<ActionResult<List<CrewDto>>> GetToday([FromQuery] string? date = null)
    {
        var workDate = DateTime.Today.Date;
        if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
            workDate = parsed.Date;

        // Pull crews for the day
        var crews = await _db.Crews
            .AsNoTracking()
            .Where(c => c.WorkDate == workDate)
            .OrderBy(c => c.TruckNumber)
            .Select(c => new CrewDto
            {
                Id = (int)c.Id,
                WorkDate = c.WorkDate,
                TruckNumber = c.TruckNumber,
                LeadTechnicianId = c.LeadTechnicianId == null ? null : (int)c.LeadTechnicianId.Value,
                Members = new List<CrewMemberDto>() // fill below
            })
            .ToListAsync();

        if (crews.Count == 0)
            return crews;

        // Pull roster members for the day in one query
        var crewIds = crews.Select(x => x.Id).ToList();

        var roster = await _db.TechnicianRosters
            .AsNoTracking()
            .Where(r => r.WorkDate == workDate && crewIds.Contains((int)r.CrewId))
            .Join(_db.Technicians.AsNoTracking(),
                  r => r.TechnicianId,
                  t => t.Id,
                  (r, t) => new
                  {
                      CrewId = (int)r.CrewId,
                      TechnicianId = (int)t.Id,
                      t.EmployeeId,
                      Name = ((t.FirstName ?? "") + " " + (t.LastName ?? "")).Trim()
                  })
            .OrderBy(x => x.Name)
            .ToListAsync();

        var byCrew = roster.GroupBy(x => x.CrewId)
                           .ToDictionary(g => g.Key, g => g
                               .Select(m => new CrewMemberDto
                               {
                                   TechnicianId = m.TechnicianId,
                                   EmployeeId = m.EmployeeId,
                                   Name = m.Name
                               }).ToList());

        foreach (var c in crews)
        {
            if (byCrew.TryGetValue(c.Id, out var members))
                c.Members = members;
        }

        return crews;
    }

    // POST /api/crews
    [HttpPost]
    public async Task<ActionResult<CrewDto>> Create([FromBody] CreateCrewRequest req)
    {
        var workDate = (req.WorkDate ?? DateTime.Today).Date;

        var entity = new CrewEntity
        {
            WorkDate = workDate,
            TruckNumber = string.IsNullOrWhiteSpace(req.TruckNumber) ? null : req.TruckNumber.Trim(),
            LeadTechnicianId = req.LeadTechnicianId == null ? null : (uint?)req.LeadTechnicianId.Value
        };

        _db.Crews.Add(entity);
        await _db.SaveChangesAsync();

        // Return the created crew (empty members for now)
        var dto = new CrewDto
        {
            Id = (int)entity.Id,
            WorkDate = entity.WorkDate,
            TruckNumber = entity.TruckNumber,
            LeadTechnicianId = entity.LeadTechnicianId == null ? null : (int)entity.LeadTechnicianId.Value,
            Members = new List<CrewMemberDto>()
        };

        return CreatedAtAction(nameof(GetToday), new { date = workDate.ToString("yyyy-MM-dd") }, dto);
    }

    // POST /api/crews/{crewId}/members/{technicianId}?date=2026-03-05
    [HttpPost("{crewId:int}/members/{technicianId:int}")]
    public async Task<IActionResult> AddMember(
        [FromRoute] int crewId,
        [FromRoute] int technicianId,
        [FromQuery] string? date = null)
    {
        var workDate = DateTime.Today.Date;
        if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed))
            workDate = parsed.Date;

        // Ensure crew exists
        var crewExists = await _db.Crews.AsNoTracking().AnyAsync(c => c.Id == (uint)crewId);
        if (!crewExists) return NotFound($"Crew {crewId} not found.");

        // Ensure tech exists + active (optional gate)
        var techExists = await _db.Technicians.AsNoTracking().AnyAsync(t => t.Id == (uint)technicianId && t.IsActive);
        if (!techExists) return NotFound($"Technician {technicianId} not found (or not active).");

        // If tech already rostered for that date, MOVE them to this crew
        var existing = await _db.TechnicianRosters
            .FirstOrDefaultAsync(r => r.WorkDate == workDate && r.TechnicianId == (uint)technicianId);

        if (existing == null)
        {
            _db.TechnicianRosters.Add(new TechnicianRosterEntity
            {
                WorkDate = workDate,
                TechnicianId = (uint)technicianId,
                CrewId = (uint)crewId
            });
        }
        else
        {
            existing.CrewId = (uint)crewId;
            _db.TechnicianRosters.Update(existing);
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }
}