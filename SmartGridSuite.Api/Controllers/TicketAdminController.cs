using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Administration;
using SmartGridSuite.Contracts.Administration.Ticket.Status;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/admin/tickets")]
    public class TicketAdminController : ControllerBase
    {
        private readonly SmartGridDbContext _db;

        public TicketAdminController(SmartGridDbContext db)
        {
            _db = db;
        }

        [HttpGet("statuses")]
        public async Task<ActionResult<List<TicketStatusDto>>> GetStatuses(CancellationToken ct)
        {
            var items = await _db.TicketStatuses
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new TicketStatusDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    SortOrder = x.SortOrder,
                    IsActive = x.IsActive,
                    IsClosed = x.IsClosed,
                    ShowInFilter = x.ShowInFilter,
                    SendToDispatchTasks = x.SendToDispatchTasks
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpGet("task-categories")]
        public async Task<ActionResult<List<TicketTaskCategoryDto>>> GetTaskCategories(CancellationToken ct)
        {
            var items = await _db.TicketTaskCategories
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new TicketTaskCategoryDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    DefaultActionRequired = x.DefaultActionRequired,
                    SortOrder = x.SortOrder,
                    IsActive = x.IsActive
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpPost("statuses")]
        public async Task<ActionResult<TicketStatusDto>> CreateStatus(
                [FromBody] CreateTicketStatusRequest request,
                CancellationToken ct)
                    {
                        var name = (request.Name ?? "").Trim();

                        if (string.IsNullOrWhiteSpace(name))
                            return BadRequest("Status name is required.");

                        var exists = await _db.TicketStatuses
                            .AsNoTracking()
                            .AnyAsync(x => x.Name.ToLower() == name.ToLower(), ct);

                        if (exists)
                            return Conflict($"A ticket status named '{name}' already exists.");

                        var entity = new TicketStatusEntity
                        {
                            Name = name,
                            SortOrder = request.SortOrder,
                            IsActive = request.IsActive,
                            IsClosed = request.IsClosed,
                            ShowInFilter = request.ShowInFilter,
                            SendToDispatchTasks = request.SendToDispatchTasks
                        };

                        _db.TicketStatuses.Add(entity);
                        await _db.SaveChangesAsync(ct);

                        var dto = new TicketStatusDto
                        {
                            Id = entity.Id,
                            Name = entity.Name,
                            SortOrder = entity.SortOrder,
                            IsActive = entity.IsActive,
                            IsClosed = entity.IsClosed,
                            ShowInFilter = entity.ShowInFilter,
                            SendToDispatchTasks = entity.SendToDispatchTasks
                        };

                        return Ok(dto);
                    }

        [HttpPut("statuses/{id:long}")]
        public async Task<ActionResult<TicketStatusDto>> UpdateStatus(
            ulong id,
            [FromBody] UpdateTicketStatusRequest request,
            CancellationToken ct)
        {
            if (id != request.Id)
                return BadRequest("Route id does not match request id.");

            var name = (request.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Status name is required.");

            var entity = await _db.TicketStatuses
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null)
                return NotFound();

            var duplicateExists = await _db.TicketStatuses
                .AsNoTracking()
                .AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct);

            if (duplicateExists)
                return Conflict($"A ticket status named '{name}' already exists.");

            entity.Name = name;
            entity.SortOrder = request.SortOrder;
            entity.IsActive = request.IsActive;
            entity.IsClosed = request.IsClosed;
            entity.ShowInFilter = request.ShowInFilter;
            entity.SendToDispatchTasks = request.SendToDispatchTasks;

            await _db.SaveChangesAsync(ct);

            var dto = new TicketStatusDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SortOrder = entity.SortOrder,
                IsActive = entity.IsActive,
                IsClosed = entity.IsClosed,
                ShowInFilter = entity.ShowInFilter,
                SendToDispatchTasks = entity.SendToDispatchTasks
            };

            return Ok(dto);
        }

        [HttpPost("statuses/{id:long}/deactivate")]
        public async Task<IActionResult> DeactivateStatus(ulong id, CancellationToken ct)
        {
            var entity = await _db.TicketStatuses
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null)
                return NotFound();

            entity.IsActive = false;
            entity.SendToDispatchTasks = false;

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        [HttpPost("task-categories")]
        public async Task<ActionResult<TicketTaskCategoryDto>> CreateTaskCategory(
                    [FromBody] CreateTicketTaskCategoryRequest request,
                    CancellationToken ct)
        {
            var name = (request.Name ?? "").Trim();
            var defaultActionRequired = (request.DefaultActionRequired ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Task category name is required.");

            if (string.IsNullOrWhiteSpace(defaultActionRequired))
                return BadRequest("Default Action Required is required.");

            var exists = await _db.TicketTaskCategories
                .AsNoTracking()
                .AnyAsync(x => x.Name.ToLower() == name.ToLower(), ct);

            if (exists)
                return Conflict($"A task category named '{name}' already exists.");

            var entity = new TicketTaskCategoryEntity
            {
                Name = name,
                DefaultActionRequired = defaultActionRequired,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            };

            _db.TicketTaskCategories.Add(entity);
            await _db.SaveChangesAsync(ct);

            var dto = new TicketTaskCategoryDto
            {
                Id = entity.Id,
                Name = entity.Name,
                DefaultActionRequired = entity.DefaultActionRequired,
                SortOrder = entity.SortOrder,
                IsActive = entity.IsActive
            };

            return Ok(dto);
        }

        [HttpPut("task-categories/{id:long}")]
        public async Task<ActionResult<TicketTaskCategoryDto>> UpdateTaskCategory(
            ulong id,
            [FromBody] UpdateTicketTaskCategoryRequest request,
            CancellationToken ct)
        {
            if (id != request.Id)
                return BadRequest("Route id does not match request id.");

            var name = (request.Name ?? "").Trim();
            var defaultActionRequired = (request.DefaultActionRequired ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Task category name is required.");

            if (string.IsNullOrWhiteSpace(defaultActionRequired))
                return BadRequest("Default Action Required is required.");

            var entity = await _db.TicketTaskCategories
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null)
                return NotFound();

            var duplicateExists = await _db.TicketTaskCategories
                .AsNoTracking()
                .AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct);

            if (duplicateExists)
                return Conflict($"A task category named '{name}' already exists.");

            entity.Name = name;
            entity.DefaultActionRequired = defaultActionRequired;
            entity.SortOrder = request.SortOrder;
            entity.IsActive = request.IsActive;

            await _db.SaveChangesAsync(ct);

            var dto = new TicketTaskCategoryDto
            {
                Id = entity.Id,
                Name = entity.Name,
                DefaultActionRequired = entity.DefaultActionRequired,
                SortOrder = entity.SortOrder,
                IsActive = entity.IsActive
            };

            return Ok(dto);
        }

        [HttpPost("task-categories/{id:long}/deactivate")]
        public async Task<IActionResult> DeactivateTaskCategory(ulong id, CancellationToken ct)
        {
            var entity = await _db.TicketTaskCategories
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null)
                return NotFound();

            entity.IsActive = false;

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }
    }
}