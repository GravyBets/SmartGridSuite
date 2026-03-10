#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Trucks;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TruckStylesController : ControllerBase
{
    private readonly SmartGridDbContext _db;

    public TruckStylesController(SmartGridDbContext db) => _db = db;

    // GET /api/truckstyles?includeInactive=true
    [HttpGet]
    public async Task<ActionResult<List<TruckStyleDto>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var q = _db.Set<TruckStyleEntity>().AsNoTracking();

        if (!includeInactive)
            q = q.Where(x => x.IsActive);

        var items = await q
            .OrderBy(x => x.Name)
            .Select(x => new TruckStyleDto
            {
                Id = (int)x.Id,
                Name = x.Name,
                IsActive = x.IsActive
            })
            .ToListAsync();

        return items;
    }

    // POST /api/truckstyles
    [HttpPost]
    public async Task<ActionResult<TruckStyleDto>> Create([FromBody] CreateTruckStyleRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0)
            return BadRequest("Name is required.");

        var exists = await _db.Set<TruckStyleEntity>()
            .AsNoTracking()
            .AnyAsync(x => x.Name == name);

        if (exists)
            return Conflict($"Truck style '{name}' already exists.");

        var entity = new TruckStyleEntity
        {
            Name = name,
            IsActive = req.IsActive
        };

        _db.Set<TruckStyleEntity>().Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new TruckStyleDto
        {
            Id = (int)entity.Id,
            Name = entity.Name,
            IsActive = entity.IsActive
        });
    }
}