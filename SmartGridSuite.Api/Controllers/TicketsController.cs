using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Tickets;
using SmartGridSuite.Contracts.Dispatcher;
using System.Text.RegularExpressions;
using SmartGridSuite.Contracts.FieldTechnician;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    public class TicketsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        public TicketsController(SmartGridDbContext db) => _db = db;

        private static readonly DateTime ActiveAssignmentDate = new(2000, 1, 1);
        private const string TechnicianRoleCode = "TECHNICIAN";

        [HttpGet]
        public async Task<ActionResult<List<TicketListItemDto>>> Get([FromQuery] string? status = null, [FromQuery] string? tech = null,
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
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
                NormalizeWorkOrderType(t.WorkOrderClass),
                t.GroupCode,
                t.PriorityDays,
                t.Problem,
                t.Notes ?? "",
                t.CreatedBy,
                t.DispatchNotes ?? ""
            )).ToList();

            return Ok(result);
        }

        [HttpPost("query")]
        public async Task<ActionResult<TicketQueryResponse>> QueryTickets([FromBody] TicketQueryRequest req, CancellationToken ct)
        {
            req ??= new TicketQueryRequest();

            var take = Math.Clamp(req.Take <= 0 ? 500 : req.Take, 1, 2000);
            var skip = Math.Max(0, req.Skip);

            var statuses = (req.Statuses ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var query = _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking()
                .AsQueryable();

            // Status filter
            if (statuses.Count > 0)
            {
                query = query.Where(t => statuses.Contains(t.Status));
            }

            // Tech filter
            var cleanTech = (req.AssignedTech ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(cleanTech) &&
                !cleanTech.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (cleanTech.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(t =>
                        string.IsNullOrWhiteSpace(t.AssignedTech) ||
                        t.AssignedTech == "(Unassigned)");
                }
                else
                {
                    query = query.Where(t => t.AssignedTech == cleanTech);
                }
            }

            // Quick filter
            var quickFilter = (req.QuickFilter ?? string.Empty).Trim();

            if (quickFilter.Equals("MissingProblems", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => string.IsNullOrWhiteSpace(t.Problem));
            }
            else if (quickFilter.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t =>
                    string.IsNullOrWhiteSpace(t.AssignedTech) ||
                    t.AssignedTech == "(Unassigned)");
            }
            else if (quickFilter.Equals("ReadyToAssign", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t =>
                    t.Status == "Open" &&
                    !string.IsNullOrWhiteSpace(t.Site) &&
                    (string.IsNullOrWhiteSpace(t.AssignedTech) ||
                     t.AssignedTech == "(Unassigned)"));
            }
            else if (quickFilter.Equals("Assigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Status == "Assigned");
            }

            // Date filter
            var dateField = (req.DateField ?? "LastActivity").Trim();

            if (req.From.HasValue)
            {
                var from = req.From.Value.Date;

                if (dateField.Equals("Created", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(t => t.CreatedAt >= from);
                else
                    query = query.Where(t => t.LastActivityAt >= from);
            }

            if (req.To.HasValue)
            {
                var toExclusive = req.To.Value.Date.AddDays(1);

                if (dateField.Equals("Created", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(t => t.CreatedAt < toExclusive);
                else
                    query = query.Where(t => t.LastActivityAt < toExclusive);
            }

            // Search filter
            var search = (req.Search ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(t =>
                    (t.Site != null && t.Site.Contains(search)) ||
                    (t.NotificationName != null && t.NotificationName.Contains(search)) ||
                    (t.Notification != null && t.Notification.Contains(search)) ||
                    (t.CurrentWorkOrder != null && t.CurrentWorkOrder.Contains(search)) ||
                    (t.WorkOrderClass != null && t.WorkOrderClass.Contains(search)) ||
                    (t.GroupCode != null && t.GroupCode.Contains(search)) ||
                    (t.Status != null && t.Status.Contains(search)) ||
                    (t.AssignedTech != null && t.AssignedTech.Contains(search)) ||
                    (t.Problem != null && t.Problem.Contains(search)) ||
                    (t.Summary != null && t.Summary.Contains(search)) ||
                    (t.Notes != null && t.Notes.Contains(search)) ||
                    (t.DispatchNotes != null && t.DispatchNotes.Contains(search)) ||
                    (t.CreatedBy != null && t.CreatedBy.Contains(search)));
            }

            var totalCount = await query.CountAsync(ct);

            var rows = await query
                .OrderByDescending(t => t.LastActivityAt)
                .ThenByDescending(t => t.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);

            var items = rows.Select(t => new TicketListItemDto(
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
                NormalizeWorkOrderType(t.WorkOrderClass),
                t.GroupCode,
                t.PriorityDays,
                t.Problem,
                t.Notes ?? "",
                t.CreatedBy,
                t.DispatchNotes ?? ""
            )).ToList();

            return Ok(new TicketQueryResponse
            {
                Items = items,
                TotalCount = totalCount
            });
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
                NormalizeWorkOrderType(t.WorkOrderClass),
                t.GroupCode,
                t.PriorityDays,
                t.Problem,
                t.Notes ?? "",
                t.CreatedBy,
                t.DispatchNotes ?? ""
            )).ToList();

            return Ok(result);
        }

        [HttpGet("field-tech/tasks/{employeeId}")]
        public async Task<ActionResult<List<FieldTechTicketListItemDto>>> GetFieldTechTasks(string employeeId, CancellationToken ct)
        {
            var tech = await ResolveActiveTechnicianByEmployeeIdAsync(employeeId, ct);

            if (tech == null)
                return Ok(new List<FieldTechTicketListItemDto>());

            var rosterDate = DateTime.Today.Date;
            var assignmentDate = ActiveAssignmentDate;

            var truckId = await _db.TruckRosters
                .AsNoTracking()
                .Where(x => x.WorkDate == rosterDate && x.TechnicianId == tech.Id)
                .Select(x => (uint?)x.TruckId)
                .FirstOrDefaultAsync(ct);

            uint targetTechnicianId = tech.Id;

            if (truckId.HasValue)
            {
                var leadTech = await ResolveLeadTechnicianForTruckAsync(
                    rosterDate,
                    truckId.Value,
                    ct);

                if (leadTech != null)
                    targetTechnicianId = leadTech.Id;
            }

            var latestTechnicianVersion = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.TargetType == "Technician" &&
                    x.TechnicianId == targetTechnicianId)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct);

            if (!latestTechnicianVersion.HasValue)
                return Ok(new List<FieldTechTicketListItemDto>());

            var publishedAssignments = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Include(x => x.Ticket)
                    .ThenInclude(t => t!.TaskCategory)
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.TargetType == "Technician" &&
                    x.TechnicianId == targetTechnicianId &&
                    x.PublishedVersion == latestTechnicianVersion.Value)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);

            var result = publishedAssignments
                .Where(x => x.Ticket != null)
                .Select(x => x.Ticket!)
                .Where(t => !IsClosedTicketStatus(t.Status))
                .Select(MapToFieldTechTicket)
                .ToList();

            return Ok(result);
        }

        [HttpGet("field-tech/history/{employeeId}")]
        public async Task<ActionResult<List<FieldTechTicketListItemDto>>> GetFieldTechHistory(string employeeId, [FromQuery] int days = 30,
            CancellationToken ct = default)
        {
            var tech = await ResolveActiveTechnicianByEmployeeIdAsync(employeeId, ct);

            if (tech == null)
                return Ok(new List<FieldTechTicketListItemDto>());

            days = Math.Clamp(days, 1, 365);

            var fromDate = DateTime.Today.AddDays(-days);
            var assignedTechValues = BuildAssignedTechMatchValues(tech);

            var rows = await _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking()
                .Where(t =>
                    assignedTechValues.Contains(t.AssignedTech) &&
                    t.LastActivityAt >= fromDate)
                .OrderByDescending(t => t.LastActivityAt)
                .ToListAsync(ct);

            var result = rows
                .Where(t => IsClosedTicketStatus(t.Status))
                .Select(MapToFieldTechTicket)
                .ToList();

            return Ok(result);
        }

        private async Task<TechnicianEntity?> ResolveActiveTechnicianByEmployeeIdAsync(string employeeId, CancellationToken ct)
        {
            employeeId = (employeeId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(employeeId))
                return null;

            return await ActiveFieldTechniciansQuery()
                .FirstOrDefaultAsync(t => t.EmployeeId == employeeId, ct);
        }

        private IQueryable<TechnicianEntity> ActiveFieldTechniciansQuery()
        {
            return _db.Technicians
                .AsNoTracking()
                .Where(t =>
                    t.IsActive &&
                    t.TechnicianRoles.Any(tr => tr.Role.Code == TechnicianRoleCode));
        }

        private async Task<TechnicianEntity?> ResolveLeadTechnicianForTruckAsync(DateTime workDate, uint truckId, CancellationToken ct)
        {
            var truck = await _db.Trucks
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == truckId && x.IsActive, ct);

            if (truck == null)
                return null;

            var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

            CrewEntity? crew = null;

            if (!string.IsNullOrWhiteSpace(truckNumber))
            {
                crew = await _db.Crews
                    .AsNoTracking()
                    .Where(x => x.WorkDate == workDate && x.TruckNumber == truckNumber)
                    .OrderBy(x => x.Id)
                    .FirstOrDefaultAsync(ct);
            }

            var technicians = await (
                from roster in _db.TruckRosters.AsNoTracking()
                join tech in ActiveFieldTechniciansQuery()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == workDate && roster.TruckId == truckId
                select tech)
                .ToListAsync(ct);

            return PickLeadTechnician(
                technicians,
                truckId,
                crew?.LeadTechnicianId);
        }

        private static TechnicianEntity? PickLeadTechnician(IReadOnlyList<TechnicianEntity> technicians, uint truckId, uint? crewLeadTechnicianId)
        {
            if (technicians.Count == 0)
                return null;

            if (crewLeadTechnicianId.HasValue)
            {
                var crewLead = technicians.FirstOrDefault(t => t.Id == crewLeadTechnicianId.Value);

                if (crewLead != null)
                    return crewLead;
            }

            var homeTruckLead = technicians.FirstOrDefault(t => t.HomeTruckId == truckId);

            if (homeTruckLead != null)
                return homeTruckLead;

            return technicians
                .OrderByDescending(GetTitleRank)
                .ThenBy(t => t.LastName)
                .ThenBy(t => t.FirstName)
                .FirstOrDefault();
        }

        private static int GetTitleRank(TechnicianEntity tech)
        {
            var title = (tech.Title ?? string.Empty).Trim();

            if (title.Equals("Supervisor", StringComparison.OrdinalIgnoreCase))
                return 400;

            if (title.Equals("Head Journeyman", StringComparison.OrdinalIgnoreCase))
                return 300;

            if (title.Equals("Journeyman", StringComparison.OrdinalIgnoreCase))
                return 200;

            if (title.Equals("Apprentice", StringComparison.OrdinalIgnoreCase))
                return 100;

            return 0;
        }

        private static List<string> BuildAssignedTechMatchValues(TechnicianEntity tech)
        {
            var fullName = FormatTechnicianName(
                tech.FirstName,
                tech.LastName,
                tech.EmployeeId);

            return new[]
                {
            tech.EmployeeId,
            fullName
        }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsClosedTicketStatus(string? status)
        {
            var cleanStatus = (status ?? string.Empty).Trim();

            return cleanStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase)
                || cleanStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                || cleanStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                || cleanStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase);
        }

        private static FieldTechTicketListItemDto MapToFieldTechTicket(TicketEntity t)
        {
            var hasActiveAssignedCategory =
                t.TaskCategory != null &&
                t.TaskCategory.IsActive &&
                !string.IsNullOrWhiteSpace(t.TaskCategory.Name);

            var actionRequired = !string.IsNullOrWhiteSpace(t.ActionRequiredOverride)
                ? t.ActionRequiredOverride.Trim()
                : hasActiveAssignedCategory && !string.IsNullOrWhiteSpace(t.TaskCategory!.DefaultActionRequired)
                    ? t.TaskCategory.DefaultActionRequired
                    : "";

            return new FieldTechTicketListItemDto
            {
                Id = t.Id,

                Site = t.Site ?? "",
                NotificationName = t.NotificationName ?? "",
                Notification = t.Notification ?? "",

                Status = string.IsNullOrWhiteSpace(t.Status) ? "Open" : t.Status,
                AssignedTech = t.AssignedTech ?? "",

                CreatedAt = t.CreatedAt,
                LastActivityAt = t.LastActivityAt,

                WorkOrder = t.CurrentWorkOrder ?? "",
                WorkOrderClass = NormalizeWorkOrderType(t.WorkOrderClass),

                GroupCode = t.GroupCode ?? "",
                PriorityDays = t.PriorityDays,

                Problem = t.Problem ?? "",
                Notes = t.Notes ?? "",

                Category = hasActiveAssignedCategory ? t.TaskCategory!.Name : "",
                ActionRequired = actionRequired
            };
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

        [HttpPost("{id:long}/resolve-dispatch-task")]
        public async Task<ActionResult<UpdateTicketResponse>> ResolveDispatchTask(long id, CancellationToken ct)
        {
            var entity = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (entity == null)
                return NotFound();

            var openStatus = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.IsActive && x.Name.ToLower() == "open",
                    ct);

            if (openStatus == null)
                return BadRequest("Status 'Open' is missing or inactive.");

            entity.Status = openStatus.Name;
            entity.TaskCategoryId = null;
            entity.ActionRequiredOverride = null;
            entity.LastActivityAt = DateTime.Now;

            entity.Notes = AppendTicketNote(
                entity.Notes,
                "Dispatch task marked done",
                "Dispatcher resolved the task.",
                "Dispatcher");

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
                TicketId = t.Id,

                OccurredAt = t.LastActivityAt != default ? t.LastActivityAt : t.CreatedAt,
                Site = t.Site ?? "",
                Tech = t.AssignedTech ?? "",
                Notification = t.Notification ?? "",
                WorkOrder = t.CurrentWorkOrder ?? "",
                WorkOrderType = NormalizeWorkOrderType(t.WorkOrderClass),
                ActionRequired = actionRequired,
                Notes = FirstNonBlank(t.DispatchNotes, t.Notes, t.Summary, t.Problem, t.NotificationName),
                Status = string.IsNullOrWhiteSpace(t.Status) ? "Open" : t.Status,
                Category = categoryName
            };
        }

        private static string NormalizeWorkOrderType(string? workOrderClass)
        {
            var value = (workOrderClass ?? string.Empty).Trim();

            if (value.Equals("Cap", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Capital", StringComparison.OrdinalIgnoreCase))
            {
                return "Capital";
            }

            if (value.Equals("Maint", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
            {
                return "Maintenance";
            }

            if (value.Equals("Dist", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Distribution", StringComparison.OrdinalIgnoreCase))
            {
                return "Distribution";
            }

            return "";
        }

        private static string? NormalizeWorkOrderClassForStorage(string? workOrderClass)
        {
            var value = (workOrderClass ?? string.Empty).Trim();

            if (value.Equals("Cap", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Capital", StringComparison.OrdinalIgnoreCase))
            {
                return "Cap";
            }

            if (value.Equals("Maint", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
            {
                return "Maint";
            }

            if (value.Equals("Dist", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Distribution", StringComparison.OrdinalIgnoreCase))
            {
                return "Dist";
            }

            return null;
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

            if (notif is not null)
            {
                var exists = await _db.Tickets.AsNoTracking().AnyAsync(t => t.Notification == notif);
                if (exists)
                    return Conflict($"A ticket already exists with Notification {notif}.");
            }

            var wo = (req.WorkOrder ?? "").Trim();            

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
                WorkOrderClass = string.IsNullOrWhiteSpace(wo)
                    ? null
                    : NormalizeWorkOrderClassForStorage(req.WorkOrderClass),
                GroupCode = (req.GroupCode ?? "").Trim(),
                PriorityDays = (byte)Math.Clamp(req.PriorityDays, 0, 255),

                TaskCategoryId = taskCategoryId,
                ActionRequiredOverride = actionRequiredOverride,

                Problem = (req.Problem ?? "").Trim(),
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
                DispatchNotes = string.IsNullOrWhiteSpace(req.DispatchNotes) ? null : req.DispatchNotes.Trim(),
                CreatedBy = createdBy,
                Summary = FirstNonBlank(req.Problem, req.NotificationName)
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

            string? notif = string.IsNullOrWhiteSpace(req.Notification) ? null : req.Notification.Trim();
                        
            if (notif is not null)
            {
                var exists = await _db.Tickets
                    .AsNoTracking()
                    .AnyAsync(t => t.Id != id && t.Notification == notif);

                if (exists)
                    return Conflict($"A ticket already exists with Notification {notif}.");
            }

            var wo = (req.WorkOrder ?? "").Trim();

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
                : NormalizeWorkOrderClassForStorage(req.WorkOrderClass);
            entity.GroupCode = string.IsNullOrWhiteSpace(wo)
                ? ""
                : (req.GroupCode ?? "").Trim();

            entity.PriorityDays = (byte)Math.Clamp(req.PriorityDays, 0, 255);

            entity.TaskCategoryId = taskCategoryId;
            entity.ActionRequiredOverride = actionRequiredOverride;

            entity.Problem = (req.Problem ?? "").Trim();
            entity.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();

            if (req.DispatchNotes != null)
            {
                entity.DispatchNotes = string.IsNullOrWhiteSpace(req.DispatchNotes)
                    ? null
                    : req.DispatchNotes.Trim();
            }

            entity.Summary = FirstNonBlank(req.Problem, req.NotificationName);
            entity.LastActivityAt = DateTime.Now;

            await _db.SaveChangesAsync();

            return Ok(new UpdateTicketResponse(entity.Id));
        }

        [HttpPost("sap-import/preview")]
        public async Task<ActionResult<List<SapQueueImportPreviewResultRow>>> PreviewSapImport([FromBody] SapQueueImportPreviewRequest req)
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
                    message = "Notification is required.";
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
                    message = "Work Order could not be read.";
                }
                else
                {
                    status = "Ready";
                    message = string.IsNullOrWhiteSpace(parsedSite)
                        ? "Site not detected — will import blank site and Needs Review."
                        : $"Site parsed as {parsedSite}. Will import as Open.";
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
        public async Task<ActionResult<SapQueueImportCommitResponse>> CommitSapImport([FromBody] SapQueueImportCommitRequest req)
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
                        "Notification is required.",
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
                        "Work Order could not be read.",
                        null));
                    continue;
                }

                var entity = new TicketEntity
                {
                    Site = parsedSite,
                    NotificationName = description,
                    Notification = notif,

                    Status = string.IsNullOrWhiteSpace(parsedSite) ? "Needs Review" : "Open",
                    AssignedTech = "(Unassigned)",

                    CreatedAt = row.NotificationDate,
                    LastActivityAt = importTime,

                    CurrentWorkOrder = string.IsNullOrWhiteSpace(workOrder) ? null : workOrder,
                    WorkOrderClass = null,
                    GroupCode = "",
                    PriorityDays = 0,

                    Problem = "",
                    Notes = null,
                    DispatchNotes = null,
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
                            : $"Imported with parsed site {parsedSite} as Open.",
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

            if (decimal.TryParse(s, out var num))
            {
                var truncated = decimal.Truncate(num);
                if (num == truncated)
                    return truncated.ToString("0");
            }

            return s;
        }

        private static string? NormalizeWorkOrder(string? raw)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (decimal.TryParse(s, out var num))
            {
                var truncated = decimal.Truncate(num);
                if (num == truncated)
                    return truncated.ToString("0");
            }

            return s;
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

            var siteHistoryWriteUp = string.IsNullOrWhiteSpace(req.SiteHistoryWriteUpText)
                ? finalWriteUp
                : req.SiteHistoryWriteUpText.Trim();

            entity.Notes = AppendTicketNote(
                entity.Notes,
                "Write-up submitted",
                finalWriteUp,
                req.SubmittedBy);

            await InsertSubmittedWriteUpIntoSiteHistoryAsync(
                entity,
                siteHistoryWriteUp,
                req.SubmittedBy,
                ct);

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

        private async Task InsertSubmittedWriteUpIntoSiteHistoryAsync(TicketEntity ticket, string siteHistoryWriteUp, string? submittedByEmployeeId,
            CancellationToken ct)
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
                {siteHistoryWriteUp},
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