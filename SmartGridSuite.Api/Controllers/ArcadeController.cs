using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Contracts.Arcade;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/arcade")]
public sealed class ArcadeController : ControllerBase
{
    private readonly SmartGridDbContext _db;

    public ArcadeController(SmartGridDbContext db)
    {
        _db = db;
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<List<ArcadeLeaderboardEntryDto>>> GetLeaderboard(
        CancellationToken ct)
    {
        var topScores = await _db.ArcadeHighScores
            .AsNoTracking()
            .Where(s => s.Technician.IsActive)
            .OrderByDescending(s => s.BestScore)
            .ThenByDescending(s => s.BestWave)
            .ThenBy(s => s.AchievedAtUtc)
            .ThenBy(s => s.TechnicianId)
            .Take(10)
            .Select(s => new
            {
                s.Technician.FirstName,
                s.Technician.LastName,
                s.BestScore,
                s.BestWave,
                s.BestRepairs,
                s.AchievedAtUtc
            })
            .ToListAsync(ct);

        var result = topScores.Select((entry, index) =>
            new ArcadeLeaderboardEntryDto
            {
                Rank = index + 1,
                TechnicianName =
                    $"{entry.FirstName} {entry.LastName}".Trim(),
                Score = entry.BestScore,
                Wave = entry.BestWave,
                Repairs = entry.BestRepairs,
                AchievedAtUtc = entry.AchievedAtUtc
            }).ToList();

        return Ok(result);
    }

    [HttpPost("scores")]
    public async Task<ActionResult<ArcadeLeaderboardEntryDto>> SubmitScore(
        [FromBody] ArcadeScoreRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("A game score is required.");

        var employeeId = (request.EmployeeId ?? "").Trim();

        if (employeeId.Length is < 1 or > 32 ||
            request.Score < 0 ||
            request.Score > 650_000 ||
            request.Wave is < 1 or > 100 ||
            request.Repairs is < 0 or > 2000 ||
            request.Score < request.Repairs * 100 ||
            request.Score > request.Repairs * 300 + request.Wave * 200)
        {
            return BadRequest("Invalid Radio Shop Arcade game score.");
        }

        // Resolve technician identity and DISPLAY NAME using the real roster.
        // Client-supplied names are neither accepted nor stored.
        var technician = await _db.Technicians
            .AsNoTracking()
            .Where(t => t.IsActive && t.EmployeeId == employeeId)
            .Select(t => new
            {
                t.Id,
                t.FirstName,
                t.LastName
            })
            .SingleOrDefaultAsync(ct);

        if (technician is null)
            return NotFound("Active technician not found in the shop roster.");

        var timestampUtc = DateTime.UtcNow;

        // MariaDB/MySQL atomic upsert: simultaneous submissions cannot replace
        // a higher personal best with a lower one. best_score is updated LAST.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO arcade_high_scores
                (technician_id, best_score, best_wave, best_repairs, achieved_at_utc)
               VALUES ({technician.Id}, {request.Score}, {request.Wave},
                       {request.Repairs}, {timestampUtc})
               ON DUPLICATE KEY UPDATE
                best_wave = IF(VALUES(best_score) > best_score,
                               VALUES(best_wave), best_wave),
                best_repairs = IF(VALUES(best_score) > best_score,
                                  VALUES(best_repairs), best_repairs),
                achieved_at_utc = IF(VALUES(best_score) > best_score,
                                     VALUES(achieved_at_utc), achieved_at_utc),
                best_score = GREATEST(best_score, VALUES(best_score))",
            ct);

        var best = await _db.ArcadeHighScores.AsNoTracking()
            .SingleAsync(s => s.TechnicianId == technician.Id, ct);

        return Ok(new ArcadeLeaderboardEntryDto
        {
            TechnicianName = $"{technician.FirstName} {technician.LastName}".Trim(),
            Score = best.BestScore,
            Wave = best.BestWave,
            Repairs = best.BestRepairs,
            AchievedAtUtc = best.AchievedAtUtc
        });
    }
}
