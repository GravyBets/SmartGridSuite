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

        private static readonly string[] RequiredTicketStatuses =
        {
            "Open",
            "Assigned",
            "In Progress",
            "Waiting Dispatch",
            "Needs Review",
            "Closed"
        };

        private static bool IsRequiredTicketStatus(string? statusName)
        {
            var clean = (statusName ?? string.Empty).Trim();

            return RequiredTicketStatuses.Contains(
                clean,
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsClosedRequiredStatus(string? statusName)
        {
            return string.Equals(
                (statusName ?? string.Empty).Trim(),
                "Closed",
                StringComparison.OrdinalIgnoreCase);
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
                    IsFieldComplete = x.IsFieldComplete,
                    ShowInFilter = x.ShowInFilter,
                    IncludeInSummary = x.IncludeInSummary,
                    SendToDispatchTasks = x.SendToDispatchTasks,
                    IsWriteUpSubmitTarget = x.IsWriteUpSubmitTarget,
                    IsAssignmentPublishTarget = x.IsAssignmentPublishTarget,
                    IsUnassignmentTarget = x.IsUnassignmentTarget
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
        public async Task<ActionResult<TicketStatusDto>> CreateStatus([FromBody] CreateTicketStatusRequest request, CancellationToken ct)
        {
            var name = (request.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Status name is required.");

            var exists = await _db.TicketStatuses
                .AsNoTracking()
                .AnyAsync(x => x.Name.ToLower() == name.ToLower(), ct);

            if (exists)
                return Conflict($"A ticket status named '{name}' already exists.");

            if (!request.IsActive &&
                (request.IsWriteUpSubmitTarget ||
                 request.IsAssignmentPublishTarget ||
                 request.IsUnassignmentTarget))
            {
                return BadRequest(
                    "A status must be active before it can be selected as a workflow target.");
            }

            var nextSortOrder =
                            (await _db.TicketStatuses.AsNoTracking().Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? 0) + 10;

            var entity = new TicketStatusEntity
            {
                Name = name,
                SortOrder = nextSortOrder,
                IsActive = request.IsActive,
                IsClosed = request.IsClosed,
                IsFieldComplete = request.IsFieldComplete,
                ShowInFilter = request.ShowInFilter,
                IncludeInSummary = request.IncludeInSummary,
                SendToDispatchTasks = request.SendToDispatchTasks,
                IsWriteUpSubmitTarget = request.IsWriteUpSubmitTarget,
                IsAssignmentPublishTarget = request.IsAssignmentPublishTarget,
                IsUnassignmentTarget = request.IsUnassignmentTarget,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _db.TicketStatuses.Add(entity);
            await _db.SaveChangesAsync(ct);

            if (entity.IsWriteUpSubmitTarget)
                await ClearOtherWriteUpSubmitTargetsAsync(entity.Id, ct);

            if (entity.IsAssignmentPublishTarget)
                await ClearOtherAssignmentPublishTargetsAsync(entity.Id, ct);

            if (entity.IsUnassignmentTarget)
                await ClearOtherUnassignmentTargetsAsync(entity.Id, ct);

            if (entity.IsWriteUpSubmitTarget ||
                entity.IsAssignmentPublishTarget ||
                entity.IsUnassignmentTarget)
            {
                await _db.SaveChangesAsync(ct);
            }

            return Ok(MapStatusDto(entity));
        }

        [HttpPut("statuses/{id:long}")]
        public async Task<ActionResult<TicketStatusDto>> UpdateStatus(ulong id, [FromBody] UpdateTicketStatusRequest request,
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

            var existingName = (entity.Name ?? string.Empty).Trim();

            if (IsRequiredTicketStatus(existingName))
            {
                if (!string.Equals(existingName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(
                        $"'{existingName}' is required by SmartGridSuite and cannot be renamed.");
                }

                if (!request.IsActive)
                {
                    return BadRequest(
                        $"'{existingName}' is required by SmartGridSuite and cannot be deactivated.");
                }

                if (IsClosedRequiredStatus(existingName) && !request.IsClosed)
                {
                    return BadRequest(
                        "'Closed' is required by SmartGridSuite and must remain marked as a closed status.");
                }

                if (!IsClosedRequiredStatus(existingName) && request.IsClosed)
                {
                    return BadRequest(
                        $"'{existingName}' is required by SmartGridSuite and cannot be marked as a closed status.");
                }
            }

            var duplicateExists = await _db.TicketStatuses
                .AsNoTracking()
                .AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), ct);

            if (duplicateExists)
                return Conflict($"A ticket status named '{name}' already exists.");

            if (!request.IsActive &&
                (request.IsWriteUpSubmitTarget ||
                 request.IsAssignmentPublishTarget ||
                 request.IsUnassignmentTarget))
            {
                return BadRequest(
                    "A status must be active before it can be selected as a workflow target.");
            }

            entity.Name = name;
            entity.IsActive = request.IsActive;
            entity.IsClosed = request.IsClosed;
            entity.IsFieldComplete = request.IsFieldComplete;
            entity.ShowInFilter = request.ShowInFilter;
            entity.IncludeInSummary = request.IncludeInSummary;
            entity.SendToDispatchTasks = request.SendToDispatchTasks;
            entity.IsWriteUpSubmitTarget = request.IsWriteUpSubmitTarget;
            entity.IsAssignmentPublishTarget = request.IsAssignmentPublishTarget;
            entity.IsUnassignmentTarget = request.IsUnassignmentTarget;
            entity.UpdatedAt = DateTime.Now;

            if (entity.IsWriteUpSubmitTarget)
                await ClearOtherWriteUpSubmitTargetsAsync(entity.Id, ct);

            if (entity.IsAssignmentPublishTarget)
                await ClearOtherAssignmentPublishTargetsAsync(entity.Id, ct);

            if (entity.IsUnassignmentTarget)
                await ClearOtherUnassignmentTargetsAsync(entity.Id, ct);

            await _db.SaveChangesAsync(ct);

            return Ok(MapStatusDto(entity));
        }

        [HttpPost("statuses/{id:long}/deactivate")]
        public async Task<IActionResult> DeactivateStatus(ulong id, CancellationToken ct)
        {
            var entity = await _db.TicketStatuses
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null)
                return NotFound();

            if (IsRequiredTicketStatus(entity.Name))
            {
                return BadRequest(
                    $"'{entity.Name}' is required by SmartGridSuite and cannot be deactivated.");
            }

            entity.IsActive = false;
            entity.ShowInFilter = false;
            entity.IncludeInSummary = false;
            entity.SendToDispatchTasks = false;
            entity.IsWriteUpSubmitTarget = false;
            entity.IsAssignmentPublishTarget = false;
            entity.IsUnassignmentTarget = false;
            entity.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        [HttpPost("statuses/{id:long}/delete")]
        public async Task<IActionResult> DeleteStatus(ulong id, CancellationToken ct)
        {
            var entity = await _db.TicketStatuses
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null)
                return NotFound();

            if (IsRequiredTicketStatus(entity.Name))
            {
                return BadRequest(
                    $"'{entity.Name}' is required by SmartGridSuite and cannot be deleted.");
            }

            var statusName = (entity.Name ?? string.Empty).Trim();

            var usedByTickets = await _db.Tickets
                .AsNoTracking()
                .AnyAsync(x => x.Status == statusName, ct);

            if (usedByTickets)
            {
                return BadRequest(
                    $"'{statusName}' is already used by one or more tickets. " +
                    "Deactivate it instead so ticket history remains intact.");
            }

            _db.TicketStatuses.Remove(entity);
            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        [HttpPost("task-categories")]
        public async Task<ActionResult<TicketTaskCategoryDto>> CreateTaskCategory([FromBody] CreateTicketTaskCategoryRequest request,
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
        public async Task<ActionResult<TicketTaskCategoryDto>> UpdateTaskCategory(ulong id, [FromBody] UpdateTicketTaskCategoryRequest request,
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

        private static TicketStatusDto MapStatusDto(TicketStatusEntity entity)
        {
            return new TicketStatusDto
            {
                Id = entity.Id,
                Name = entity.Name,
                SortOrder = entity.SortOrder,
                IsActive = entity.IsActive,
                IsClosed = entity.IsClosed,
                IsFieldComplete = entity.IsFieldComplete,
                ShowInFilter = entity.ShowInFilter,
                IncludeInSummary = entity.IncludeInSummary,
                SendToDispatchTasks = entity.SendToDispatchTasks,
                IsWriteUpSubmitTarget = entity.IsWriteUpSubmitTarget,
                IsAssignmentPublishTarget = entity.IsAssignmentPublishTarget,
                IsUnassignmentTarget = entity.IsUnassignmentTarget
            };
        }

        private async Task ClearOtherWriteUpSubmitTargetsAsync(ulong currentStatusId, CancellationToken ct)
        {
            var otherTargets = await _db.TicketStatuses
                .Where(x => x.Id != currentStatusId && x.IsWriteUpSubmitTarget)
                .ToListAsync(ct);

            foreach (var status in otherTargets)
                status.IsWriteUpSubmitTarget = false;
        }

        private async Task ClearOtherAssignmentPublishTargetsAsync(ulong currentStatusId, CancellationToken ct)
        {
            var otherTargets = await _db.TicketStatuses
                .Where(x => x.Id != currentStatusId && x.IsAssignmentPublishTarget)
                .ToListAsync(ct);

            foreach (var status in otherTargets)
                status.IsAssignmentPublishTarget = false;
        }

        private async Task ClearOtherUnassignmentTargetsAsync(ulong currentStatusId, CancellationToken ct)
        {
            var otherTargets = await _db.TicketStatuses
                .Where(x => x.Id != currentStatusId && x.IsUnassignmentTarget)
                .ToListAsync(ct);

            foreach (var status in otherTargets)
                status.IsUnassignmentTarget = false;
        }
    }
}