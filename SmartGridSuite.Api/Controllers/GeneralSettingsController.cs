using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Settings;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/admin/general-settings")]
    public sealed class GeneralSettingsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;

        public GeneralSettingsController(SmartGridDbContext db)
        {
            _db = db;
        }

        [HttpGet("communication-device-types")]
        public async Task<ActionResult<List<CommunicationDeviceTypeDto>>> GetCommunicationDeviceTypes(
            [FromQuery] bool activeOnly = false,
            CancellationToken ct = default)
        {
            var query = _db.CommunicationDeviceTypes.AsNoTracking();

            if (activeOnly)
                query = query.Where(x => x.IsActive);

            var items = await query
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .Select(x => new CommunicationDeviceTypeDto
                {
                    Id = x.Id,
                    DisplayName = x.DisplayName,
                    IsActive = x.IsActive,
                    SortOrder = x.SortOrder
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpPost("communication-device-types")]
        public async Task<ActionResult<CommunicationDeviceTypeDto>> CreateCommunicationDeviceType(
            [FromBody] SaveCommunicationDeviceTypeRequest request,
            CancellationToken ct)
        {
            var displayName = (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                return BadRequest("Display name is required.");

            var alreadyExists = await _db.CommunicationDeviceTypes
                .AnyAsync(x => x.DisplayName == displayName, ct);

            if (alreadyExists)
                return Conflict("A communication device type with that name already exists.");

            var entity = new CommunicationDeviceTypeEntity
            {
                DisplayName = displayName,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _db.CommunicationDeviceTypes.Add(entity);
            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(entity));
        }

        [HttpPut("communication-device-types/{id:int}")]
        public async Task<ActionResult<CommunicationDeviceTypeDto>> UpdateCommunicationDeviceType(
            int id,
            [FromBody] SaveCommunicationDeviceTypeRequest request,
            CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest("Invalid communication device type id.");

            var entityId = (uint)id;

            var entity = await _db.CommunicationDeviceTypes
                .FirstOrDefaultAsync(x => x.Id == entityId, ct);

            if (entity is null)
                return NotFound();

            var displayName = (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                return BadRequest("Display name is required.");

            var alreadyExists = await _db.CommunicationDeviceTypes
                .AnyAsync(x => x.Id != entityId && x.DisplayName == displayName, ct);

            if (alreadyExists)
                return Conflict("A communication device type with that name already exists.");

            entity.DisplayName = displayName;
            entity.IsActive = request.IsActive;
            entity.SortOrder = request.SortOrder;
            entity.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(entity));
        }

        private static CommunicationDeviceTypeDto ToDto(CommunicationDeviceTypeEntity entity)
        {
            return new CommunicationDeviceTypeDto
            {
                Id = entity.Id,
                DisplayName = entity.DisplayName,
                IsActive = entity.IsActive,
                SortOrder = entity.SortOrder
            };
        }
    }
}