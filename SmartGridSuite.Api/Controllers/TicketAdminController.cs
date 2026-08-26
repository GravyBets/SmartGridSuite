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
            "Needs Review",
            "Closed"
        };

        private const int TicketStatusNameMaxLength = 100;

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
                    IsWriteUpSubmitTarget = x.IsWriteUpSubmitTarget
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

            if (name.Length > TicketStatusNameMaxLength)
                return BadRequest($"Status name must be {TicketStatusNameMaxLength} characters or less.");

            var exists = await _db.TicketStatuses
                .AsNoTracking()
                .AnyAsync(x => x.Name.ToLower() == name.ToLower(), ct);

            if (exists)
                return Conflict($"A ticket status named '{name}' already exists.");

            if (!request.IsActive && request.IsWriteUpSubmitTarget)
            {
                return BadRequest(
                    "A status must be active before it can be selected as the Write-Up Target.");
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
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _db.TicketStatuses.Add(entity);
            await _db.SaveChangesAsync(ct);

            if (entity.IsWriteUpSubmitTarget)
            {
                await ClearOtherWriteUpSubmitTargetsAsync(
                    entity.Id,
                    ct);

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

            if (name.Length > TicketStatusNameMaxLength)
                return BadRequest($"Status name must be {TicketStatusNameMaxLength} characters or less.");

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

            if (!request.IsActive && request.IsWriteUpSubmitTarget)
            {
                return BadRequest(
                    "A status must be active before it can be selected as the Write-Up Target.");
            }

            var statusNameChanged =
    !string.Equals(
        existingName,
        name,
        StringComparison.Ordinal);

            await using var transaction =
                await _db.Database.BeginTransactionAsync(ct);

            entity.Name = name;
            entity.IsActive = request.IsActive;
            entity.IsClosed = request.IsClosed;
            entity.IsFieldComplete = request.IsFieldComplete;
            entity.ShowInFilter = request.ShowInFilter;
            entity.IncludeInSummary = request.IncludeInSummary;
            entity.SendToDispatchTasks = request.SendToDispatchTasks;
            entity.IsWriteUpSubmitTarget = request.IsWriteUpSubmitTarget;
            entity.UpdatedAt = DateTime.Now;

            /*
             * Ticket status is currently stored as the status name on each ticket.
             * If an administrator renames a configurable status, migrate every
             * existing ticket using the old name so those tickets do not become
             * orphaned from the configured status list.
             *
             * This is a metadata rename, not ticket activity, so do not change
             * LastActivityAt or add Dispatch Notes.
             */
            if (statusNameChanged)
            {
                await _db.Tickets
                    .Where(x => x.Status == existingName)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                name),
                        ct);
            }

            if (entity.IsWriteUpSubmitTarget)
                await ClearOtherWriteUpSubmitTargetsAsync(entity.Id, ct);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Ok(MapStatusDto(entity));
        }

        [HttpPut("statuses/reorder")]
        public async Task<IActionResult> ReorderStatuses([FromBody] ReorderTicketStatusesRequest request, CancellationToken ct)
        {
            var requestedIds = (request.OrderedIds ?? new List<ulong>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (requestedIds.Count == 0)
                return BadRequest("At least one status id is required.");

            var statuses = await _db.TicketStatuses
                .ToListAsync(ct);

            var statusById = statuses.ToDictionary(x => x.Id);

            var missingIds = requestedIds
                .Where(x => !statusById.ContainsKey(x))
                .ToList();

            if (missingIds.Count > 0)
                return BadRequest("One or more status ids were not found.");

            /*
             * The client normally sends every row, but append anything missing just in case.
             * This keeps sort order normalized for all statuses.
             */
            var finalIds = requestedIds
                .Concat(
                    statuses
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Name)
                        .Select(x => x.Id)
                        .Where(x => !requestedIds.Contains(x)))
                .ToList();

            for (var i = 0; i < finalIds.Count; i++)
            {
                var status = statusById[finalIds[i]];
                status.SortOrder = (i + 1) * 10;
                status.UpdatedAt = DateTime.Now;
            }

            await _db.SaveChangesAsync(ct);

            return NoContent();
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
                IsWriteUpSubmitTarget = entity.IsWriteUpSubmitTarget
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
    }
}