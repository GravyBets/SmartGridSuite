using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Tickets;
using SmartGridSuite.Contracts.Dispatcher;
using System.Text.RegularExpressions;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    public class TicketsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        public TicketsController(SmartGridDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<List<TicketListItemDto>>> Get(
            [FromQuery] string? status = null,
            [FromQuery] string? tech = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            var q = _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);

            if (!string.IsNullOrWhiteSpace(tech))
                q = q.Where(t => t.AssignedTech == tech);

            if (from.HasValue)
                q = q.Where(t => t.LastActivityAt >= from.Value.Date);

            if (to.HasValue)
            {
                var toEndExclusive = to.Value.Date.AddDays(1);
                q = q.Where(t => t.LastActivityAt < toEndExclusive);
            }

            var rows = await q
                .OrderByDescending(t => t.LastActivityAt)                
                .ToListAsync();

            var result = rows.Select(t => new TicketListItemDto(
                t.Id,
                t.Site,
                t.NotificationName ?? "",
                t.Notification ?? "",
                t.Status,
                t.TaskCategoryId,
                t.TaskCategory != null ? t.TaskCategory.Name : null,
                t.ActionRequiredOverride,
                t.AssignedTech,
                t.CreatedAt,
                t.LastActivityAt,
                t.CurrentWorkOrder ?? "",
                string.IsNullOrWhiteSpace(t.WorkOrderClass) ? "Maint" : t.WorkOrderClass!,
                t.GroupCode,
                t.PriorityDays,
                t.Problem,
                t.Notes ?? "",
                t.CreatedBy
            )).ToList();

            return Ok(result);
        }


        [HttpGet("dispatch-tasks")]
        public async Task<ActionResult<List<DispatchTaskListItemDto>>> GetDispatchTasks(CancellationToken ct)
        {
            var dispatchStatuses = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive && x.SendToDispatchTasks)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Name)
                .ToListAsync(ct);

            if (dispatchStatuses.Count == 0)
                return Ok(new List<DispatchTaskListItemDto>());

            var reviewCategory = await _db.TicketTaskCategories
                .AsNoTracking()
                .Where(x => x.IsActive && x.Name == "Review")
                .Select(x => new
                {
                    x.Name,
                    x.DefaultActionRequired
                })
                .FirstOrDefaultAsync(ct);

            var fallbackCategoryName = reviewCategory?.Name ?? "Review";
            var fallbackActionRequired = reviewCategory?.DefaultActionRequired ?? "Review and update ticket";

            var tickets = await _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking()
                .Where(t => dispatchStatuses.Contains(t.Status))
                .OrderByDescending(t => t.LastActivityAt)
                .ToListAsync(ct);

            var items = tickets
                .Select(t => MapToDispatchTask(t, fallbackCategoryName, fallbackActionRequired))
                .ToList();

            return Ok(items);
        }


        [HttpGet("by-site/{siteId}")]
        public async Task<ActionResult<List<TicketListItemDto>>> GetBySite(string siteId, CancellationToken ct)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return Ok(new List<TicketListItemDto>());

            var normalizedSiteId = siteId
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();

            var rows = await _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking()
                .Where(t =>
                    t.Site != null &&
                    t.Site.Replace("_", "").Replace("-", "").Replace(" ", "").ToUpper() == normalizedSiteId)
                .OrderByDescending(t => t.LastActivityAt)
                .ToListAsync(ct);

            var result = rows.Select(t => new TicketListItemDto(
                t.Id,
                t.Site,
                t.NotificationName ?? "",
                t.Notification ?? "",
                t.Status,
                t.TaskCategoryId,
                t.TaskCategory != null ? t.TaskCategory.Name : null,
                t.ActionRequiredOverride,
                t.AssignedTech,
                t.CreatedAt,
                t.LastActivityAt,
                t.CurrentWorkOrder ?? "",
                string.IsNullOrWhiteSpace(t.WorkOrderClass) ? "Maint" : t.WorkOrderClass!,
                t.GroupCode,
                t.PriorityDays,
                t.Problem,
                t.Notes ?? "",
                t.CreatedBy
            )).ToList();

            return Ok(result);
        }


        [HttpPost("{id:long}/request-capital")]
        public async Task<ActionResult<UpdateTicketResponse>> RequestCapital(long id, [FromBody] TicketActionReasonRequest req, CancellationToken ct)
        {
            var entity = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null)
                return NotFound();

            var reason = (req.Reason ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest("Reason is required.");

            var awaitingCapitalStatus = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.IsActive && x.Name.ToLower() == "awaiting capital",
                    ct);

            if (awaitingCapitalStatus == null)
                return BadRequest("Status 'Awaiting Capital' is missing or inactive.");

            entity.Status = awaitingCapitalStatus.Name;
            entity.ActionRequiredOverride = "Review Capital request";
            entity.Notes = AppendTicketNote(
                entity.Notes,
                "Capital requested",
                reason,
                req.RequestedBy);

            entity.LastActivityAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(new UpdateTicketResponse(entity.Id));
        }


        [HttpPost("{id:long}/request-maintenance")]
        public async Task<ActionResult<UpdateTicketResponse>> RequestMaintenance(long id, [FromBody] TicketActionReasonRequest req, CancellationToken ct)
        {
            var entity = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null)
                return NotFound();

            var reason = (req.Reason ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest("Reason is required.");

            var needsReviewStatus = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.IsActive && x.Name.ToLower() == "needs review",
                    ct);

            if (needsReviewStatus == null)
                return BadRequest("Status 'Needs Review' is missing or inactive.");

            entity.Status = needsReviewStatus.Name;
            entity.ActionRequiredOverride = "Review Maintenance request";
            entity.Notes = AppendTicketNote(
                entity.Notes,
                "Maintenance requested",
                reason,
                req.RequestedBy);

            entity.LastActivityAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(new UpdateTicketResponse(entity.Id));
        }


        private static DispatchTaskListItemDto MapToDispatchTask(TicketEntity t, string fallbackCategoryName, string fallbackActionRequired)
        {
            var hasActiveAssignedCategory =
                t.TaskCategory != null &&
                t.TaskCategory.IsActive &&
                !string.IsNullOrWhiteSpace(t.TaskCategory.Name);

            var categoryName = hasActiveAssignedCategory
                ? t.TaskCategory!.Name
                : fallbackCategoryName;

            var actionRequired = !string.IsNullOrWhiteSpace(t.ActionRequiredOverride)
                ? t.ActionRequiredOverride.Trim()
                : hasActiveAssignedCategory && !string.IsNullOrWhiteSpace(t.TaskCategory!.DefaultActionRequired)
                    ? t.TaskCategory.DefaultActionRequired
                    : fallbackActionRequired;

            return new DispatchTaskListItemDto
            {
                OccurredAt = t.LastActivityAt != default ? t.LastActivityAt : t.CreatedAt,
                Site = t.Site ?? "",
                Tech = t.AssignedTech ?? "",
                Notification = t.Notification ?? "",
                WorkOrder = t.CurrentWorkOrder ?? "",
                WorkOrderType = NormalizeWorkOrderType(t.WorkOrderClass),
                ActionRequired = actionRequired,
                Notes = FirstNonBlank(t.Notes, t.Summary, t.Problem, t.NotificationName),
                Status = string.IsNullOrWhiteSpace(t.Status) ? "Open" : t.Status,
                Category = categoryName
            };
        }

        private static string NormalizeWorkOrderType(string? workOrderClass)
        {
            return string.Equals(workOrderClass, "Cap", StringComparison.OrdinalIgnoreCase)
                ? "Capital"
                : "Maintenance";
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string AppendTicketNote(string? existingNotes, string action, string reason, string? requestedBy)
        {
            var cleanExisting = (existingNotes ?? string.Empty).Trim();
            var cleanAction = string.IsNullOrWhiteSpace(action) ? "Ticket action" : action.Trim();
            var cleanReason = (reason ?? string.Empty).Trim();
            var cleanRequestedBy = string.IsNullOrWhiteSpace(requestedBy)
                ? "Unknown"
                : requestedBy.Trim();

            var entry =
                $"[{DateTime.Now:MM-dd-yyyy HH:mm}] {cleanAction} by {cleanRequestedBy}{Environment.NewLine}" +
                $"Reason: {cleanReason}";

            if (string.IsNullOrWhiteSpace(cleanExisting))
                return entry;

            return cleanExisting + Environment.NewLine + Environment.NewLine + entry;
        }


        [HttpPost]
        public async Task<ActionResult<CreateTicketResponse>> Create([FromBody] CreateTicketRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Site)) return BadRequest("Site required");
            if (string.IsNullOrWhiteSpace(req.Problem)) return BadRequest("Problem required");

            var incomingNotificationName = (req.NotificationName ?? string.Empty).Trim();

            if (incomingNotificationName.Equals(
                    "Ticket requested from Site Dashboard",
                    StringComparison.OrdinalIgnoreCase))
            {
                var site = req.Site.Trim();

                var existingDashboardRequest = await _db.Tickets
                    .AsNoTracking()
                    .Where(t =>
                        t.Site == site &&
                        t.NotificationName == "Ticket requested from Site Dashboard" &&
                        t.Status != "Closed" &&
                        t.Status != "Completed" &&
                        t.Status != "Cancelled" &&
                        t.Status != "Canceled")
                    .OrderByDescending(t => t.LastActivityAt)
                    .FirstOrDefaultAsync();

                if (existingDashboardRequest is not null)
                    return Ok(new CreateTicketResponse(existingDashboardRequest.Id));
            }

            string? notif = string.IsNullOrWhiteSpace(req.Notification) ? null : req.Notification.Trim();

            if (notif is not null && !Regex.IsMatch(notif, @"^\d{10}$"))
                return BadRequest("Notification must be 10 digits when provided");

            if (notif is not null)
            {
                var exists = await _db.Tickets.AsNoTracking().AnyAsync(t => t.Notification == notif);
                if (exists)
                    return Conflict($"A ticket already exists with Notification {notif}.");
            }

            var wo = (req.WorkOrder ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(wo) && !Regex.IsMatch(wo, @"^\d{9}$"))
                return BadRequest("WorkOrder must be 9 digits when provided");

            ulong? taskCategoryId = req.TaskCategoryId;
            string? actionRequiredOverride = string.IsNullOrWhiteSpace(req.ActionRequiredOverride)
                ? null
                : req.ActionRequiredOverride.Trim();

            if (taskCategoryId.HasValue)
            {
                var categoryExists = await _db.TicketTaskCategories
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == taskCategoryId.Value && x.IsActive);

                if (!categoryExists)
                    return BadRequest("Selected task category is invalid or inactive.");
            }

            var assignedTech = string.IsNullOrWhiteSpace(req.AssignedTech) ? "(Unassigned)" : req.AssignedTech.Trim();

            var requestedStatus = (req.Status ?? "").Trim();
            if (string.IsNullOrWhiteSpace(requestedStatus))
                return BadRequest("Status required");

            var requestedStatusLower = requestedStatus.ToLower();

            var statusEntity = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsActive && x.Name.ToLower() == requestedStatusLower);

            if (statusEntity == null)
                return BadRequest("Selected status is invalid or inactive.");

            var status = statusEntity.Name;

            var createdBy = string.IsNullOrWhiteSpace(req.CreatedBy) ? "Unknown" : req.CreatedBy.Trim();

            var now = DateTime.Now;

            var entity = new TicketEntity
            {
                Site = req.Site.Trim(),
                NotificationName = (req.NotificationName ?? "").Trim(),
                Notification = notif,

                Status = status,
                AssignedTech = assignedTech,

                CreatedAt = now,
                LastActivityAt = now,

                CurrentWorkOrder = string.IsNullOrWhiteSpace(wo) ? null : wo,
                WorkOrderClass = string.IsNullOrWhiteSpace(wo) ? null : (req.WorkOrderClass ?? "Maint").Trim(),
                GroupCode = (req.GroupCode ?? "").Trim(),
                PriorityDays = (byte)Math.Clamp(req.PriorityDays, 0, 255),

                TaskCategoryId = taskCategoryId,
                ActionRequiredOverride = actionRequiredOverride,

                Problem = req.Problem.Trim(),
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
                CreatedBy = createdBy,
                Summary = req.Problem.Trim()
            };

            try
            {
                _db.Tickets.Add(entity);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                if (msg.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
                    return Conflict($"A ticket already exists with Notification {notif}.");

                throw;
            }

            return Ok(new CreateTicketResponse(entity.Id));
        }

        [HttpPost("{id:long}/update")]
        public async Task<ActionResult<UpdateTicketResponse>> Update(long id, [FromBody] UpdateTicketRequest req)
        {
            var entity = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
            if (entity == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(req.Site))
                return BadRequest("Site required");

            if (string.IsNullOrWhiteSpace(req.Problem))
                return BadRequest("Problem required");

            string? notif = string.IsNullOrWhiteSpace(req.Notification) ? null : req.Notification.Trim();

            if (notif is not null && !Regex.IsMatch(notif, @"^\d{10}$"))
                return BadRequest("Notification must be 10 digits when provided");

            if (notif is not null)
            {
                var exists = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id != id && t.Notification == notif);

                if (exists)
                    return Conflict($"A ticket already exists with Notification {notif}.");
            }

            var wo = (req.WorkOrder ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(wo) && !Regex.IsMatch(wo, @"^\d{9}$"))
                return BadRequest("WorkOrder must be 9 digits when provided");

            ulong? taskCategoryId = req.TaskCategoryId;
            string? actionRequiredOverride = string.IsNullOrWhiteSpace(req.ActionRequiredOverride)
                ? null
                : req.ActionRequiredOverride.Trim();

            if (taskCategoryId.HasValue)
            {
                var categoryExists = await _db.TicketTaskCategories
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == taskCategoryId.Value && x.IsActive);

                if (!categoryExists)
                    return BadRequest("Selected task category is invalid or inactive.");
            }

            var assignedTech = string.IsNullOrWhiteSpace(req.AssignedTech)
                ? "(Unassigned)"
                : req.AssignedTech.Trim();

            var requestedStatus = (req.Status ?? "").Trim();
            if (string.IsNullOrWhiteSpace(requestedStatus))
                return BadRequest("Status required");

            var requestedStatusLower = requestedStatus.ToLower();

            var statusEntity = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsActive && x.Name.ToLower() == requestedStatusLower);

            if (statusEntity == null)
                return BadRequest("Selected status is invalid or inactive.");

            var status = statusEntity.Name;

            entity.Site = req.Site.Trim();
            entity.NotificationName = (req.NotificationName ?? "").Trim();
            entity.Notification = notif;

            entity.Status = status;
            entity.AssignedTech = assignedTech;

            entity.CurrentWorkOrder = string.IsNullOrWhiteSpace(wo) ? null : wo;
            entity.WorkOrderClass = string.IsNullOrWhiteSpace(wo)
                ? null
                : (req.WorkOrderClass ?? "").Trim();
            entity.GroupCode = string.IsNullOrWhiteSpace(wo)
                ? ""
                : (req.GroupCode ?? "").Trim();

            entity.PriorityDays = (byte)Math.Clamp(req.PriorityDays, 0, 255);

            entity.TaskCategoryId = taskCategoryId;
            entity.ActionRequiredOverride = actionRequiredOverride;

            entity.Problem = req.Problem.Trim();
            entity.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
            entity.Summary = req.Problem.Trim();
            entity.LastActivityAt = DateTime.Now;

            await _db.SaveChangesAsync();

            return Ok(new UpdateTicketResponse(entity.Id));
        }

        [HttpPost("sap-import/preview")]
        public async Task<ActionResult<List<SapQueueImportPreviewResultRow>>> PreviewSapImport(
            [FromBody] SapQueueImportPreviewRequest req)
        {
            var rows = req.Rows ?? new List<SapQueueImportPreviewRow>();
            if (rows.Count == 0)
                return Ok(new List<SapQueueImportPreviewResultRow>());

            var incomingNotifications = rows
                .Select(r => NormalizeNotification(r.Notification))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            var existingNotifications = new HashSet<string>(
                await _db.Tickets.AsNoTracking()
                    .Where(t => t.Notification != null && incomingNotifications.Contains(t.Notification!))
                    .Select(t => t.Notification!)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<SapQueueImportPreviewResultRow>();

            foreach (var row in rows)
            {
                var rawNotification = (row.Notification ?? "").Trim();
                var notif = NormalizeNotification(rawNotification);
                var workOrder = NormalizeWorkOrder(row.WorkOrder);
                var description = (row.Description ?? "").Trim();
                var parsedSite = TryParseSiteFromDescription(description);

                string status;
                string message;

                if (string.IsNullOrWhiteSpace(notif))
                {
                    status = "Invalid";
                    message = "Notification must be exactly 10 digits.";
                }
                else if (!seenInFile.Add(notif))
                {
                    status = "Invalid";
                    message = "Duplicate notification appears more than once in this import file.";
                }
                else if (existingNotifications.Contains(notif))
                {
                    status = "Already Exists";
                    message = $"Notification {notif} already exists.";
                }
                else if (row.NotificationDate is null)
                {
                    status = "Invalid";
                    message = "Notif.date is missing or invalid.";
                }
                else if (string.IsNullOrWhiteSpace(description))
                {
                    status = "Invalid";
                    message = "Description is required.";
                }
                else if (!string.IsNullOrWhiteSpace(row.WorkOrder) && string.IsNullOrWhiteSpace(workOrder))
                {
                    status = "Invalid";
                    message = "Work Order must be exactly 9 digits when provided.";
                }
                else
                {
                    status = "Ready";
                    message = string.IsNullOrWhiteSpace(parsedSite)
                        ? "Site not detected — will import blank site and Needs Review."
                        : $"Site parsed as {parsedSite}.";
                }

                result.Add(new SapQueueImportPreviewResultRow(
                    RowNumber: row.RowNumber,
                    Notification: rawNotification,
                    WorkOrder: string.IsNullOrWhiteSpace(workOrder) ? row.WorkOrder?.Trim() : workOrder,
                    NotificationDate: row.NotificationDate,
                    Description: description,
                    ParsedSite: parsedSite,
                    ImportStatus: status,
                    Message: message
                ));
            }

            return Ok(result);
        }


        [HttpPost("sap-import/commit")]
        public async Task<ActionResult<SapQueueImportCommitResponse>> CommitSapImport(
            [FromBody] SapQueueImportCommitRequest req)
        {
            var rows = req.Rows ?? new List<SapQueueImportCommitRow>();
            var createdBy = string.IsNullOrWhiteSpace(req.CreatedBy) ? "Unknown" : req.CreatedBy.Trim();
            var importTime = DateTime.Now;

            if (rows.Count == 0)
            {
                return Ok(new SapQueueImportCommitResponse(
                    ImportedCount: 0,
                    AlreadyExistsCount: 0,
                    InvalidCount: 0,
                    Rows: new()));
            }

            var incomingNotifications = rows
                .Select(r => NormalizeNotification(r.Notification))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            var existingNotifications = new HashSet<string>(
                await _db.Tickets.AsNoTracking()
                    .Where(t => t.Notification != null && incomingNotifications.Contains(t.Notification!))
                    .Select(t => t.Notification!)
                    .ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var results = new List<SapQueueImportCommitResultRow>();
            int imported = 0;
            int alreadyExists = 0;
            int invalid = 0;

            foreach (var row in rows)
            {
                var rawNotification = (row.Notification ?? "").Trim();
                var notif = NormalizeNotification(rawNotification);
                var workOrder = NormalizeWorkOrder(row.WorkOrder);
                var description = (row.Description ?? "").Trim();
                var parsedSite = string.IsNullOrWhiteSpace(row.ParsedSite)
                    ? TryParseSiteFromDescription(description)
                    : row.ParsedSite.Trim();

                if (string.IsNullOrWhiteSpace(notif))
                {
                    invalid++;
                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        rawNotification,
                        "Invalid",
                        "Notification must be exactly 10 digits.",
                        null));
                    continue;
                }

                if (!seenInFile.Add(notif))
                {
                    invalid++;
                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        notif,
                        "Invalid",
                        "Duplicate notification appears more than once in this import file.",
                        null));
                    continue;
                }

                if (existingNotifications.Contains(notif))
                {
                    alreadyExists++;
                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        notif,
                        "Already Exists",
                        $"Notification {notif} already exists.",
                        null));
                    continue;
                }

                if (row.NotificationDate == default)
                {
                    invalid++;
                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        notif,
                        "Invalid",
                        "Notif.date is missing or invalid.",
                        null));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    invalid++;
                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        notif,
                        "Invalid",
                        "Description is required.",
                        null));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.WorkOrder) && string.IsNullOrWhiteSpace(workOrder))
                {
                    invalid++;
                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        notif,
                        "Invalid",
                        "Work Order must be exactly 9 digits when provided.",
                        null));
                    continue;
                }

                var entity = new TicketEntity
                {
                    Site = parsedSite,
                    NotificationName = description,
                    Notification = notif,

                    Status = "Needs Review",
                    AssignedTech = "(Unassigned)",

                    CreatedAt = row.NotificationDate,
                    LastActivityAt = importTime,

                    CurrentWorkOrder = string.IsNullOrWhiteSpace(workOrder) ? null : workOrder,
                    WorkOrderClass = null,
                    GroupCode = "",
                    PriorityDays = 0,

                    Problem = "",
                    Notes = null,
                    CreatedBy = createdBy,
                    Summary = description
                };

                try
                {
                    _db.Tickets.Add(entity);
                    await _db.SaveChangesAsync();

                    existingNotifications.Add(notif);
                    imported++;

                    results.Add(new SapQueueImportCommitResultRow(
                        row.RowNumber,
                        notif,
                        "Imported",
                        string.IsNullOrWhiteSpace(parsedSite)
                            ? "Imported with blank site. Dispatch review required."
                            : $"Imported with parsed site {parsedSite}.",
                        entity.Id));
                }
                catch (DbUpdateException ex)
                {
                    _db.Entry(entity).State = EntityState.Detached;

                    var msg = ex.InnerException?.Message ?? ex.Message;
                    if (msg.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
                    {
                        existingNotifications.Add(notif);
                        alreadyExists++;

                        results.Add(new SapQueueImportCommitResultRow(
                            row.RowNumber,
                            notif,
                            "Already Exists",
                            $"Notification {notif} already exists.",
                            null));
                    }
                    else
                    {
                        invalid++;

                        results.Add(new SapQueueImportCommitResultRow(
                            row.RowNumber,
                            notif,
                            "Invalid",
                            "Import failed for this row.",
                            null));
                    }
                }
            }

            return Ok(new SapQueueImportCommitResponse(
                ImportedCount: imported,
                AlreadyExistsCount: alreadyExists,
                InvalidCount: invalid,
                Rows: results));
        }

        private static string? NormalizeNotification(string? raw)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (Regex.IsMatch(s, @"^\d{10}$"))
                return s;

            if (decimal.TryParse(s, out var num))
            {
                var truncated = decimal.Truncate(num);
                if (num == truncated)
                {
                    var normalized = truncated.ToString("0");
                    if (Regex.IsMatch(normalized, @"^\d{10}$"))
                        return normalized;
                }
            }

            return null;
        }

        private static string? NormalizeWorkOrder(string? raw)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (Regex.IsMatch(s, @"^\d{9}$"))
                return s;

            if (decimal.TryParse(s, out var num))
            {
                var truncated = decimal.Truncate(num);
                if (num == truncated)
                {
                    var normalized = truncated.ToString("0");
                    if (Regex.IsMatch(normalized, @"^\d{9}$"))
                        return normalized;
                }
            }

            return null;
        }

        private static string TryParseSiteFromDescription(string? description)
        {
            var text = (description ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddSiteMatches(candidates, text, @"(?<![A-Za-z0-9])(G\d{4})(?![A-Za-z0-9])");
            AddSiteMatches(candidates, text, @"(?<![A-Za-z0-9])(\d{4}MR)(?![A-Za-z0-9])");
            AddSiteMatches(candidates, text, @"(?<![A-Za-z0-9])(RX\d{4})(?![A-Za-z0-9])");

            return candidates.Count == 1
                ? candidates.First().ToUpperInvariant()
                : "";
        }

        private static void AddSiteMatches(HashSet<string> candidates, string text, string pattern)
        {
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
            {
                if (match.Groups.Count > 1)
                {
                    var value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        candidates.Add(value.ToUpperInvariant());
                }
            }
        }

        [HttpPost("{id:long}/submit-writeup")]
        public async Task<ActionResult<UpdateTicketResponse>> SubmitWriteUp(long id, [FromBody] SubmitTicketWriteUpRequest req, CancellationToken ct)
        {
            var entity = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);

            if (entity == null)
                return NotFound();

            var finalWriteUp = (req.FinalWriteUpText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(finalWriteUp))
                return BadRequest("Write-up text is required.");

            entity.Notes = AppendRawTicketNote(entity.Notes, finalWriteUp);

            await InsertSubmittedWriteUpIntoSiteHistoryAsync(entity, finalWriteUp, req.SubmittedBy, ct);

            entity.ActionRequiredOverride = "Review submitted site write-up";
            entity.LastActivityAt = DateTime.Now;

            var needsReviewStatus = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.IsActive && x.Name.ToLower() == "needs review",
                    ct);

            if (needsReviewStatus != null)
                entity.Status = needsReviewStatus.Name;

            await _db.SaveChangesAsync(ct);

            return Ok(new UpdateTicketResponse(entity.Id));
        }

        private static string AppendRawTicketNote(string? existingNotes, string noteToAppend)
        {
            var existing = (existingNotes ?? string.Empty).TrimEnd();
            var next = (noteToAppend ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(existing))
                return next;

            if (string.IsNullOrWhiteSpace(next))
                return existing;

            return existing +
                   Environment.NewLine +
                   Environment.NewLine +
                   next;
        }

        private async Task InsertSubmittedWriteUpIntoSiteHistoryAsync(TicketEntity ticket, string finalWriteUp, string? submittedByEmployeeId, CancellationToken ct)
        {
            var siteId = (ticket.Site ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return;

            var crew = await ResolveSubmittedCrewAsync(submittedByEmployeeId, ct);

            var sourceType = "SmartGridSuite";
            var sourceFile = $"Ticket {ticket.Id}";
            var visitDate = DateTime.Today;

            var primaryTech = TrimForColumn(crew.PrimaryTech, 100);
            var secondaryTech = TrimForColumn(crew.SecondaryTech, 100);
            var issueText = ticket.Problem ?? string.Empty;

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO site_history
            (
                legacy_source_id,
                source_type,
                source_file,
                site_id,
                visit_date,
                primary_tech,
                secondary_tech,
                narrative,
                issue_text
            )
            VALUES
            (
                NULL,
                {sourceType},
                {sourceFile},
                {siteId},
                {visitDate},
                {primaryTech},
                {secondaryTech},
                {finalWriteUp},
                {issueText}
            );", ct);
        }

        private async Task<SubmittedCrewInfo> ResolveSubmittedCrewAsync( string? submittedByEmployeeId, CancellationToken ct)
        {
            var employeeId = (submittedByEmployeeId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(employeeId))
            {
                return new SubmittedCrewInfo
                {
                    PrimaryTech = "Unknown",
                    SecondaryTech = null
                };
            }

            var workDate = DateTime.Today;

            var submitter = await _db.Technicians
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.IsActive, ct);

            if (submitter is null)
            {
                return new SubmittedCrewInfo
                {
                    PrimaryTech = employeeId,
                    SecondaryTech = null
                };
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
                return new SubmittedCrewInfo
                {
                    PrimaryTech = primaryName,
                    SecondaryTech = null
                };
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
                    tech.LastName
                })
                .ToListAsync(ct);

            var secondaryNames = crewTechs
                .Where(x => x.Id != submitter.Id)
                .Select(x => FormatTechnicianName(
                    x.FirstName,
                    x.LastName,
                    x.EmployeeId))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            return new SubmittedCrewInfo
            {
                PrimaryTech = primaryName,
                SecondaryTech = secondaryNames.Count == 0
                    ? null
                    : FormatCrewDisplayText(secondaryNames)
            };
        }

        private sealed class SubmittedCrewInfo
        {
            public string PrimaryTech { get; set; } = "";
            public string? SecondaryTech { get; set; }
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
                return string.Empty;

            if (cleanNames.Count == 1)
                return cleanNames[0];

            if (cleanNames.Count == 2)
                return $"{cleanNames[0]} & {cleanNames[1]}";

            return string.Join(", ", cleanNames.Take(cleanNames.Count - 1)) +
                   " & " +
                   cleanNames.Last();
        }

        private static string? TrimForColumn(string? value, int maxLength)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Length <= maxLength
                ? text
                : text[..maxLength];
        }

    }
}