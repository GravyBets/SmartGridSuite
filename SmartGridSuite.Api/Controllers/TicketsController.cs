using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Dispatcher;
using SmartGridSuite.Contracts.FieldTechnician;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Tickets;
using System.Text.RegularExpressions;
using System.Net;
using System.Text;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/tickets")]
    public class TicketsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        private readonly TruckBoardInitializationService _truckBoardInitialization;
        private readonly EmailService _emailService;

        private readonly DailyAssignmentEmailSequenceService
            _dailyAssignmentEmailSequence;

        private readonly ILogger<TicketsController> _logger;

        private static readonly DateTime ActiveAssignmentDate = new(2000, 1, 1);
        private const string TechnicianRoleCode = "TECHNICIAN";

        private const string TicketSiteRequiredMessage =
            "A Site Number is required before this ticket can be saved. " +
            "You may enter the Problem / Issue first, but Smart Grid Suite " +
            "cannot create or update the ticket until it is tied to a site. " +
            "Enter the Site Number and save again. " +
            "For TOP sites, enter only the TOP site, such as XX-MWB. " +
            "Do not include the sector.";

        // Receives the shared roster initializer so field tasks can safely build today's crew route.
        public TicketsController(
            SmartGridDbContext db,
            TruckBoardInitializationService truckBoardInitialization,
            EmailService emailService,
            DailyAssignmentEmailSequenceService dailyAssignmentEmailSequence,
            ILogger<TicketsController> logger)
        {
            _db =
                db;

            _truckBoardInitialization =
                truckBoardInitialization;

            _emailService =
                emailService;

            _dailyAssignmentEmailSequence =
                dailyAssignmentEmailSequence;

            _logger =
                logger;
        }

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
            if (req.ApplyStatusFilter)
            {
                if (statuses.Count == 0)
                {
                    query = query.Where(t => false);
                }
                else
                {
                    query = query.Where(t => statuses.Contains(t.Status));
                }
            }
            else if (statuses.Count > 0)
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
                /*
                 * Preserve the exact timestamp supplied by the client.
                 * This keeps rolling ranges such as Last 24 Hours accurate.
                 * Custom DatePicker values already arrive at midnight.
                 */
                var from = req.From.Value;

                if (dateField.Equals(
                        "Created",
                        StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(
                        t => t.CreatedAt >= from);
                }
                else
                {
                    query = query.Where(
                        t => t.LastActivityAt >= from);
                }
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

        [HttpGet("summary")]
        public async Task<ActionResult<TicketSummaryDto>> GetSummary(CancellationToken ct)
        {
            var configuredStatuses = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive && x.IncludeInSummary)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Name,
                    x.SortOrder
                })
                .ToListAsync(ct);

            var countsByStatus = await _db.Tickets
                .AsNoTracking()
                .GroupBy(x => x.Status)
                .Select(g => new
                {
                    Status = g.Key ?? "",
                    Count = g.Count()
                })
                .ToDictionaryAsync(
                    x => x.Status,
                    x => x.Count,
                    StringComparer.OrdinalIgnoreCase,
                    ct);

            var totalCount = await _db.Tickets
                .AsNoTracking()
                .CountAsync(ct);

            return Ok(new TicketSummaryDto
            {
                TotalCount = totalCount,
                Statuses = configuredStatuses
                    .Select(x => new TicketSummaryStatusDto
                    {
                        Status = x.Name,
                        SortOrder = x.SortOrder,
                        Count = countsByStatus.TryGetValue(x.Name, out var count)
                            ? count
                            : 0
                    })
                    .ToList()
            });
        }

        [HttpGet("filter-statuses")]
        public async Task<ActionResult<List<TicketFilterStatusDto>>> GetFilterStatuses(CancellationToken ct)
        {
            var statuses = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive && x.ShowInFilter)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .Select(x => new TicketFilterStatusDto
                {
                    Name = x.Name,
                    SortOrder = x.SortOrder,
                    IsClosed = x.IsClosed
                })
                .ToListAsync(ct);

            return Ok(statuses);
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

        [HttpPost("dispatch-tasks/query")]
        public async Task<ActionResult<DispatchTaskQueryResponse>> QueryDispatchTasks([FromBody] DispatchTaskQueryRequest req,
            CancellationToken ct)
        {
            req ??= new DispatchTaskQueryRequest();

            var take = Math.Clamp(req.Take <= 0 ? 500 : req.Take, 1, 2000);
            var skip = Math.Max(0, req.Skip);

            var requestedStatuses = (req.Statuses ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var taskStatuses = _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive && x.SendToDispatchTasks)
                .Select(x => x.Name);

            var query = _db.Tickets
                .AsNoTracking()
                .Where(t => taskStatuses.Contains(t.Status));

            // Optional status filter inside the set of statuses configured for Tasks.
            if (req.ApplyStatusFilter)
            {
                if (requestedStatuses.Count == 0)
                {
                    query = query.Where(t => false);
                }
                else
                {
                    query = query.Where(t => requestedStatuses.Contains(t.Status));
                }
            }

            // Technician filter.
            var assignedTech = (req.AssignedTech ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(assignedTech) &&
                !assignedTech.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (assignedTech.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(t =>
                        string.IsNullOrWhiteSpace(t.AssignedTech) ||
                        t.AssignedTech == "(Unassigned)");
                }
                else
                {
                    query = query.Where(t => t.AssignedTech == assignedTech);
                }
            }

            // Date filter based on last task/ticket activity.
            if (req.From.HasValue)
            {
                var from = req.From.Value.Date;
                query = query.Where(t => t.LastActivityAt >= from);
            }

            if (req.To.HasValue)
            {
                var toExclusive = req.To.Value.Date.AddDays(1);
                query = query.Where(t => t.LastActivityAt < toExclusive);
            }

            // Search.
            var search = (req.Search ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(t =>
                    (t.Site != null && t.Site.Contains(search)) ||
                    (t.NotificationName != null && t.NotificationName.Contains(search)) ||
                    (t.Notification != null && t.Notification.Contains(search)) ||
                    (t.CurrentWorkOrder != null && t.CurrentWorkOrder.Contains(search)) ||
                    (t.WorkOrderClass != null && t.WorkOrderClass.Contains(search)) ||
                    (t.AssignedTech != null && t.AssignedTech.Contains(search)) ||
                    (t.Problem != null && t.Problem.Contains(search)) ||
                    (t.ActionRequiredOverride != null && t.ActionRequiredOverride.Contains(search)) ||
                    (t.DispatchNotes != null && t.DispatchNotes.Contains(search)) ||
                    (t.Notes != null && t.Notes.Contains(search)) ||
                    (t.Status != null && t.Status.Contains(search)));
            }

            var totalCount = await query.CountAsync(ct);

            var rows = await query
                .OrderByDescending(t => t.LastActivityAt)
                .ThenByDescending(t => t.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);

            var items = rows
                .Select(MapToDispatchTaskQueryItem)
                .ToList();

            return Ok(new DispatchTaskQueryResponse
            {
                Items = items,
                TotalCount = totalCount
            });
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

        [HttpGet("{id:long}")]
        public async Task<ActionResult<TicketListItemDto>> GetById(long id, CancellationToken ct)
        {
            var ticket = await _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (ticket == null)
                return NotFound();

            return Ok(new TicketListItemDto(
                ticket.Id,
                ticket.Site,
                ticket.NotificationName ?? "",
                ticket.Notification ?? "",
                ticket.Status,
                ticket.TaskCategoryId,
                ticket.TaskCategory != null ? ticket.TaskCategory.Name : null,
                ticket.ActionRequiredOverride,
                ticket.AssignedTech,
                ticket.CreatedAt,
                ticket.LastActivityAt,
                ticket.CurrentWorkOrder ?? "",
                NormalizeWorkOrderType(ticket.WorkOrderClass),
                ticket.GroupCode,
                ticket.PriorityDays,
                ticket.Problem,
                ticket.Notes ?? "",
                ticket.CreatedBy,
                ticket.DispatchNotes ?? ""
            ));
        }

        // Loads expandable field-row details on demand so Tasks and History stay
        // lightweight while still displaying current Dispatch Notes and active Site Notes.
        [HttpGet("field-tech/expanded-details/{ticketId:long}")]
        public async Task<ActionResult<FieldTechExpandedTicketDetailsDto>> GetFieldTechExpandedDetails(
            long ticketId, CancellationToken ct)
        {
            var ticket = await _db.Tickets
                .AsNoTracking()
                .Where(x => x.Id == ticketId)
                .Select(x => new
                {
                    x.Id,
                    x.Site,
                    x.DispatchNotes
                })
                .FirstOrDefaultAsync(ct);

            if (ticket == null)
                return NotFound();

            var normalizedSiteId = NormalizeSiteIdForFieldDetails(ticket.Site);

            var siteNotes = new List<FieldTechSiteNoteDto>();

            if (!string.IsNullOrWhiteSpace(normalizedSiteId))
            {
                /*
                 * These filtering and ordering rules intentionally match
                 * SiteNotesController.GetBySite(...) so field row details show the
                 * same current active notes as the Site Dashboard.
                 */
                siteNotes = await _db.SiteNotes
                    .AsNoTracking()
                    .Where(x => x.IsActive && x.SiteId == normalizedSiteId)
                    .OrderBy(x => x.NoteType)
                    .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                    .Select(x => new FieldTechSiteNoteDto
                    {
                        Id = x.Id,
                        NoteType = x.NoteType ?? "",
                        NoteText = x.NoteText,
                        CreatedBy = x.CreatedBy,
                        CreatedAt = x.CreatedAt,
                        UpdatedBy = x.UpdatedBy ?? "",
                        UpdatedAt = x.UpdatedAt
                    })
                    .ToListAsync(ct);
            }

            return Ok(new FieldTechExpandedTicketDetailsDto
            {
                TicketId = ticket.Id,
                Site = ticket.Site ?? "",
                DispatchNotes = ticket.DispatchNotes ?? "",
                SiteNotes = siteNotes
            });
        }

        // Builds both Field Technician task sections in the API so the UI only displays
        // published route work and deduplicated direct assignments already in business-rule order.
        [HttpGet("field-tech/tasks/{employeeId}")]
        public async Task<ActionResult<FieldTechTasksResponseDto>> GetFieldTechTasks(string employeeId, CancellationToken ct)
        {
            var tech = await ResolveActiveTechnicianByEmployeeIdAsync(employeeId, ct);

            if (tech == null)
                return Ok(new FieldTechTasksResponseDto());

            var rosterDate = DateTime.Today.Date;
            var assignmentDate = ActiveAssignmentDate;

            /*
             * Field Technician Tasks may be the first workflow opened in the morning.
             * Ensure today's carried-forward truck/crew roster exists before resolving
             * the technician's published crew route.
             */
            await _truckBoardInitialization.EnsureBoardInitializedAsync(rosterDate, ct);

            var statusRows = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new
                {
                    x.Name,
                    x.IsClosed,
                    x.IsFieldComplete
                })
                .ToListAsync(ct);

            var closedStatusNames = statusRows
                .Where(x => x.IsClosed)
                .Select(x => x.Name)
                .ToList();

            var closedStatusSet = closedStatusNames
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fieldCompleteStatusSet = statusRows
                .Where(x => x.IsFieldComplete)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var publishedAssignments = new List<DailyTicketAssignmentPublishedEntity>();

            var truckId = await _db.TruckRosters
                .AsNoTracking()
                .Where(x => x.WorkDate == rosterDate && x.TechnicianId == tech.Id)
                .Select(x => (uint?)x.TruckId)
                .FirstOrDefaultAsync(ct);

            bool signedInTechIsCrewLead = false;

            if (truckId.HasValue)
            {
                var leadTech = await ResolveLeadTechnicianForTruckAsync(
                    rosterDate,
                    truckId.Value,
                    ct);

                if (leadTech != null)
                {
                    signedInTechIsCrewLead = leadTech.Id == tech.Id;

                    /*
                     * A technician in a truck sees the route owned by that truck's lead.
                     * If the signed-in tech is not the lead, do not also load their old
                     * individual route.
                     */
                    publishedAssignments.AddRange(
                        await LoadLatestPublishedTechnicianTargetAsync(
                            assignmentDate,
                            leadTech.Id,
                            truckId.Value,
                            ct));
                }
            }
            else
            {
                /*
                 * A technician not currently assigned to a truck sees their individual route.
                 */
                publishedAssignments.AddRange(
                    await LoadLatestPublishedTechnicianTargetAsync(
                        assignmentDate,
                        tech.Id,
                        truckId: null,
                        ct));
            }

            /*
             * If the signed-in tech is the lead, their lead route was already loaded above.
             * If they are not in a truck, their individual route was loaded above.
             * If they are in a truck under another lead, they should not see their old
             * individual route.
             */

            var orderedPublishedAssignments = publishedAssignments
                .Where(x => x.Ticket != null)
                .Where(x => !closedStatusSet.Contains(x.Ticket!.Status ?? string.Empty))
                .GroupBy(x => x.TicketId)
                .Select(x => x.First())
                .ToList();

            var dailyAssignmentTicketIds = orderedPublishedAssignments
                .Select(x => x.TicketId)
                .Distinct()
                .ToList();

            var assignedTechValues = BuildAssignedTechMatchValues(tech);

            var technicianDisplayName =
                FormatTechnicianName(
                    tech.FirstName,
                    tech.LastName,
                    tech.EmployeeId);

            var technicianEmployeeId =
                (tech.EmployeeId ?? string.Empty).Trim();

            /*
             * This section intentionally contains only tickets directly assigned to the
             * signed-in technician and excludes work already present in the published route.
             *
             * Direct dispatch assignment is stored as a display string. Support both the
             * older single-tech exact value and the newer multi-tech formatted value such as
             * "Alex Smith, Pat Jones & Lee Brown".
             */
            var otherAssignedQuery = _db.Tickets
                .Include(t => t.TaskCategory)
                .AsNoTracking()
                .Where(t =>
                    !closedStatusNames.Contains(t.Status) &&
                    t.AssignedTech != null &&
                    (
                        assignedTechValues.Contains(t.AssignedTech) ||
                        t.AssignedTech.Contains(technicianDisplayName) ||
                        (
                            technicianEmployeeId != "" &&
                            t.AssignedTech.Contains(technicianEmployeeId)
                        )
                    ));

            if (dailyAssignmentTicketIds.Count > 0)
            {
                otherAssignedQuery = otherAssignedQuery
                    .Where(t => !dailyAssignmentTicketIds.Contains(t.Id));
            }

            var otherAssignedRows = await otherAssignedQuery
                .OrderBy(t => t.PriorityDays == 0 ? 999 : t.PriorityDays)
                .ThenByDescending(t => t.LastActivityAt)
                .ThenByDescending(t => t.Id)
                .ToListAsync(ct);

            var allTaskSites = orderedPublishedAssignments
                .Where(x => x.Ticket != null)
                .Select(x => NormalizeSiteIdForFieldDetails(x.Ticket!.Site))
                .Concat(otherAssignedRows.Select(x => NormalizeSiteIdForFieldDetails(x.Site)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var siteNoteCountsBySite = allTaskSites.Count == 0
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : await _db.SiteNotes
                    .AsNoTracking()
                    .Where(x => x.IsActive && allTaskSites.Contains(x.SiteId))
                    .GroupBy(x => x.SiteId)
                    .Select(g => new
                    {
                        SiteId = g.Key,
                        Count = g.Count()
                    })
                    .ToDictionaryAsync(
                        x => x.SiteId,
                        x => x.Count,
                        StringComparer.OrdinalIgnoreCase,
                        ct);

            var dailyAssignments = new List<FieldTechTicketListItemDto>();
            var routeOrder = 0;

            foreach (var assignment in orderedPublishedAssignments)
            {
                routeOrder++;

                dailyAssignments.Add(
                    MapToFieldTechTicket(
                        assignment.Ticket!,
                        fieldCompleteStatusSet,
                        routeOrder,
                        siteNoteCountsBySite));
            }

            var otherAssignedTickets = otherAssignedRows
                .Select(t => MapToFieldTechTicket(
                    t,
                    fieldCompleteStatusSet,
                    routeOrder: null,
                    siteNoteCountsBySite))
                .ToList();

            return Ok(new FieldTechTasksResponseDto
            {
                TechnicianName = FormatTechnicianName(
                    tech.FirstName,
                    tech.LastName,
                    tech.EmployeeId),

                DailyAssignments = dailyAssignments,
                OtherAssignedTickets = otherAssignedTickets
            });
        }

        // Loads the latest published snapshot for one technician-owned route.
        // TechnicianId is the route owner. TruckId is display/crew context only and must
        // not split the route identity.
        private async Task<List<DailyTicketAssignmentPublishedEntity>> LoadLatestPublishedTechnicianTargetAsync(
            DateTime assignmentDate,
            uint technicianId,
            uint? truckId,
            CancellationToken ct)
        {
            var latestPublishedVersion = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.TargetType == "Technician" &&
                    x.TechnicianId == technicianId)
                .Select(x => (int?)x.PublishedVersion)
                .MaxAsync(ct);

            if (!latestPublishedVersion.HasValue)
                return new List<DailyTicketAssignmentPublishedEntity>();

            return await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Include(x => x.Ticket)
                    .ThenInclude(t => t!.TaskCategory)
                .Where(x =>
                    x.AssignmentDate == assignmentDate &&
                    x.TargetType == "Technician" &&
                    x.TechnicianId == technicianId &&
                    x.PublishedVersion == latestPublishedVersion.Value)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .ToListAsync(ct);
        }

        // Returns completed work in which the signed-in technician participated.
        // Completion date comes from the write-up submission, while current Work Order
        // values continue to come from the live ticket for accurate time entry.
        [HttpPost("field-tech/history/{employeeId}/query")]
        public async Task<ActionResult<FieldTechHistoryQueryResponse>> QueryFieldTechHistory(string employeeId,
            [FromBody] FieldTechHistoryQueryRequest? req, CancellationToken ct)
        {
            req ??= new FieldTechHistoryQueryRequest();

            var today = DateTime.Today.Date;
            var oldestAllowedDate = today.AddDays(-364);

            var appliedTo = req.To?.Date ?? today;

            if (appliedTo > today)
                appliedTo = today;

            if (appliedTo < oldestAllowedDate)
            {
                return BadRequest(
                    "History is available for the most recent 365 days only.");
            }

            var appliedFrom = req.From?.Date ?? appliedTo.AddDays(-29);

            if (appliedFrom < oldestAllowedDate)
                appliedFrom = oldestAllowedDate;

            if (appliedFrom > appliedTo)
                return BadRequest("The history start date cannot be after the end date.");

            var technician = await ResolveActiveTechnicianByEmployeeIdAsync(
                employeeId,
                ct);

            if (technician == null)
            {
                return Ok(new FieldTechHistoryQueryResponse
                {
                    AppliedFrom = appliedFrom,
                    AppliedTo = appliedTo
                });
            }

            var take = Math.Clamp(
                req.Take <= 0 ? 500 : req.Take,
                1,
                2000);

            var skip = Math.Max(0, req.Skip);

            var toExclusive = appliedTo.AddDays(1);

            var technicianEmployeeId = (technician.EmployeeId ?? string.Empty).Trim();

            /*
             * A submission belongs in this technician's History when the technician
             * was snapshotted as a participant in the assigned work. The technician who
             * pressed Submit is not the sole owner of the completed-work record.
             *
             * Current ticket assignment and closure status are intentionally ignored
             * because those values may change after the field work was completed.
             */
            var query =
                from submission in _db.TicketWriteUpSubmissions.AsNoTracking()
                join ticket in _db.Tickets.AsNoTracking()
                    on submission.TicketId equals ticket.Id
                where !submission.IsDeleted
                      && submission.SubmittedAt >= appliedFrom
                      && submission.SubmittedAt < toExclusive
                      && _db.TicketWriteUpSubmissionTechnicians
                          .AsNoTracking()
                          .Any(participant =>
                              participant.SubmissionId == submission.Id &&
                              (
                                  participant.TechnicianId == technician.Id ||
                                  participant.EmployeeId == technicianEmployeeId
                              ))
                select new
                {
                    Submission = submission,
                    Ticket = ticket
                };

            var search = (req.Search ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    (x.Ticket.Site != null &&
                     x.Ticket.Site.Contains(search)) ||

                    (x.Ticket.NotificationName != null &&
                     x.Ticket.NotificationName.Contains(search)) ||

                    (x.Ticket.Notification != null &&
                     x.Ticket.Notification.Contains(search)) ||

                    (x.Ticket.CurrentWorkOrder != null &&
                     x.Ticket.CurrentWorkOrder.Contains(search)) ||

                    (x.Ticket.WorkOrderClass != null &&
                     x.Ticket.WorkOrderClass.Contains(search)) ||

                    (x.Ticket.Status != null &&
                     x.Ticket.Status.Contains(search)) ||

                    (x.Ticket.Problem != null &&
                     x.Ticket.Problem.Contains(search)) ||

                    (x.Submission.SubmittedNarrative != null &&
                     x.Submission.SubmittedNarrative.Contains(search)));
            }

            var totalCount = await query.CountAsync(ct);

            /*
             * Work-order counts are calculated by the API from the live ticket record.
             * This ensures the copy workflow reports the current valid WO after any
             * Maintenance-to-Capital conversion.
             */
            var itemsWithWorkOrderCount = await query.CountAsync(
                x => x.Ticket.CurrentWorkOrder != null &&
                     x.Ticket.CurrentWorkOrder != "",
                ct);

            var rows = await query
                .OrderByDescending(x => x.Submission.SubmittedAt)
                .ThenByDescending(x => x.Submission.Id)
                .Skip(skip)
                .Take(take)
                .Select(x => new
                {
                    x.Submission.Id,
                    x.Submission.TicketId,
                    x.Submission.SiteHistoryId,
                    x.Submission.SubmittedAt,
                    x.Submission.SubmittedNarrative,
                    x.Submission.SubmittedByName,

                    x.Ticket.Site,
                    x.Ticket.NotificationName,
                    x.Ticket.Notification,
                    x.Ticket.CurrentWorkOrder,
                    x.Ticket.WorkOrderClass,
                    x.Ticket.Status,
                    x.Ticket.Problem
                })
                .ToListAsync(ct);

            var items = rows
                .Select(x => new FieldTechHistoryItemDto
                {
                    SubmissionId = x.Id,
                    TicketId = x.TicketId,
                    SiteHistoryId = x.SiteHistoryId,

                    CompletedAt = x.SubmittedAt,

                    Site = x.Site ?? "",
                    NotificationName = x.NotificationName ?? "",
                    Notification = x.Notification ?? "",

                    /*
                     * These values intentionally come from the current ticket row,
                     * not the write-up submission record.
                     */
                    CurrentWorkOrder = x.CurrentWorkOrder ?? "",
                    CurrentWorkOrderType = NormalizeWorkOrderType(x.WorkOrderClass),

                    CurrentStatus = string.IsNullOrWhiteSpace(x.Status)
                        ? "Open"
                        : x.Status,

                    Problem = x.Problem ?? "",
                    SubmittedNarrative = x.SubmittedNarrative ?? "",
                    SubmittedByName = x.SubmittedByName ?? ""
                })
                .ToList();

            return Ok(new FieldTechHistoryQueryResponse
            {
                TechnicianName = FormatTechnicianName(
                    technician.FirstName,
                    technician.LastName,
                    technician.EmployeeId),

                AppliedFrom = appliedFrom,
                AppliedTo = appliedTo,

                Items = items,
                TotalCount = totalCount,

                ItemsWithWorkOrderCount = itemsWithWorkOrderCount,
                ItemsWithoutWorkOrderCount = totalCount - itemsWithWorkOrderCount
            });
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

        // Maps a ticket row for Field Technician display and supplies API-calculated
        // completion and route-order values rather than asking WPF to infer them.
        private static FieldTechTicketListItemDto MapToFieldTechTicket(TicketEntity t, HashSet<string>? fieldCompleteStatusNames, int? routeOrder,
            IReadOnlyDictionary<string, int>? siteNoteCountsBySite = null)
        {
            var hasActiveAssignedCategory =
                t.TaskCategory != null &&
                t.TaskCategory.IsActive &&
                !string.IsNullOrWhiteSpace(t.TaskCategory.Name);

            var actionRequired = !string.IsNullOrWhiteSpace(t.ActionRequiredOverride)
                ? t.ActionRequiredOverride.Trim()
                : hasActiveAssignedCategory &&
                  !string.IsNullOrWhiteSpace(t.TaskCategory!.DefaultActionRequired)
                    ? t.TaskCategory.DefaultActionRequired
                    : "";

            var normalizedSiteId = NormalizeSiteIdForFieldDetails(t.Site);

            var siteNoteCount =
                !string.IsNullOrWhiteSpace(normalizedSiteId) &&
                siteNoteCountsBySite != null &&
                siteNoteCountsBySite.TryGetValue(normalizedSiteId, out var count)
                    ? count
                    : 0;

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
                ActionRequired = actionRequired,

                IsFieldComplete =
                    fieldCompleteStatusNames?.Contains(t.Status ?? string.Empty) == true,

                RouteOrder = routeOrder,

                HasDispatchNotes = !string.IsNullOrWhiteSpace(t.DispatchNotes),
                SiteNoteCount = siteNoteCount
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

            entity.DispatchNotes = AppendDispatchRequestNote(
                entity.DispatchNotes,
                "Capital",
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

            entity.DispatchNotes = AppendDispatchRequestNote(
                entity.DispatchNotes,
                "Maintenance",
                reason,
                req.RequestedBy);

            entity.LastActivityAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(new UpdateTicketResponse(entity.Id));
        }

        [HttpPost("{id:long}/close-dispatch-task")]
        public async Task<ActionResult<UpdateTicketResponse>> CloseDispatchTask(long id, CancellationToken ct)
        {
            var entity = await _db.Tickets
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (entity == null)
                return NotFound();

            var closedStatuses = await _db.TicketStatuses
                .AsNoTracking()
                .Where(x => x.IsActive && x.IsClosed)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToListAsync(ct);

            var closedStatus = closedStatuses.FirstOrDefault(x =>
                string.Equals(x.Name, "Closed", StringComparison.OrdinalIgnoreCase));

            if (closedStatus == null && closedStatuses.Count == 1)
                closedStatus = closedStatuses[0];

            if (closedStatus == null)
            {
                if (closedStatuses.Count == 0)
                {
                    return BadRequest(
                        "No active closed ticket status is configured. " +
                        "Go to Administration > Tickets and configure an active status as Closed.");
                }

                return BadRequest(
                    "More than one active closed ticket status is configured and none is named 'Closed'. " +
                    "Configure one active closed status named 'Closed' for dispatcher closure actions.");
            }

            entity.Status = closedStatus.Name;

            // These are active follow-up fields; once dispatch closes the ticket,
            // it should no longer carry an outstanding task action.
            entity.TaskCategoryId = null;
            entity.ActionRequiredOverride = null;

            entity.LastActivityAt = DateTime.Now;

            // Dispatcher activity belongs in Dispatch Notes, not technician write-ups.
            entity.DispatchNotes = AppendTicketNote(
                entity.DispatchNotes,
                "Ticket closed",
                "Dispatcher closed the ticket from the Tasks pane.",
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

        private static DispatchTaskListItemDto MapToDispatchTaskQueryItem(TicketEntity ticket)
        {
            var actionRequired = FirstNonBlank(
                ticket.ActionRequiredOverride,
                "Review ticket");

            return new DispatchTaskListItemDto
            {
                TicketId = ticket.Id,

                OccurredAt = ticket.LastActivityAt != default
                    ? ticket.LastActivityAt
                    : ticket.CreatedAt,

                Site = ticket.Site ?? "",
                NotificationName = ticket.NotificationName ?? "",
                Problem = ticket.Problem ?? "",

                Tech = ticket.AssignedTech ?? "",

                Notification = ticket.Notification ?? "",
                WorkOrder = ticket.CurrentWorkOrder ?? "",
                WorkOrderType = NormalizeWorkOrderType(ticket.WorkOrderClass),

                ActionRequired = actionRequired,

                Notes = FirstNonBlank(
                    ticket.DispatchNotes,
                    ticket.Notes,
                    ticket.Problem,
                    ticket.NotificationName),

                Status = string.IsNullOrWhiteSpace(ticket.Status)
                    ? "Open"
                    : ticket.Status,

                // Legacy category administration is no longer used by the new Tasks query.
                Category = ""
            };
        }

        // Normalizes ticket site IDs using the same rule as SiteNotesController so
        // expandable field rows retrieve the same active notes as Site Dashboard.
        private static string NormalizeSiteIdForFieldDetails(string? siteId)
        {
            return (siteId ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
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

        private static string AppendDispatchRequestNote(string? existingNotes, string requestType, string reason, string? requestedBy)
        {
            var cleanExisting = (existingNotes ?? string.Empty).Trim();

            var cleanRequestType = string.IsNullOrWhiteSpace(requestType)
                ? "Ticket"
                : requestType.Trim();

            var cleanReason = (reason ?? string.Empty).Trim();

            var cleanRequestedBy = string.IsNullOrWhiteSpace(requestedBy)
                ? "Unknown"
                : requestedBy.Trim();

            var entry =
                $"{cleanRequestType} requested by {cleanRequestedBy} Reason: '{cleanReason}'";

            if (string.IsNullOrWhiteSpace(cleanExisting))
                return entry;

            return cleanExisting + Environment.NewLine + entry;
        }

        [HttpPost]
        public async Task<ActionResult<CreateTicketResponse>> Create([FromBody] CreateTicketRequest req)
        {
            var createValidationError = ValidateCreateTicketRequest(req);

            if (!string.IsNullOrWhiteSpace(createValidationError))
                return BadRequest(createValidationError);

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

            var isSiteDashboardTicketRequest = incomingNotificationName.Equals("Ticket requested from Site Dashboard", StringComparison.OrdinalIgnoreCase);

            var requestDispatchNotes = isSiteDashboardTicketRequest
                ? AppendDispatchRequestNote(req.DispatchNotes, "Ticket", FirstNonBlank(req.Notes, req.Problem, "Ticket requested from Site Dashboard."),
                    createdBy)
                : req.DispatchNotes;

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
                Notes = isSiteDashboardTicketRequest
                    ? null
                    : string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),

                DispatchNotes = string.IsNullOrWhiteSpace(requestDispatchNotes)
                    ? null
                    : requestDispatchNotes.Trim(),
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

                if (IsColumnTooLongException(ex, "current_work_order"))
                    return BadRequest("Work Order is too long. Work Order is limited to 10 characters.");

                return BadRequest("Ticket could not be saved because one or more fields are too long for the database.");
            }

            return Ok(new CreateTicketResponse(entity.Id));
        }

        [HttpPost("{id:long}/update")]
        public async Task<ActionResult<UpdateTicketResponse>> Update(long id, [FromBody] UpdateTicketRequest req, CancellationToken ct)
        {
            var entity = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
            if (entity == null)
                return NotFound();

            var originalWorkOrder = entity.CurrentWorkOrder;
            var originalWorkOrderClass = entity.WorkOrderClass;

            var updateValidationError = ValidateUpdateTicketRequest(req);

            if (!string.IsNullOrWhiteSpace(updateValidationError))
                return BadRequest(updateValidationError);

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

            var workOrderChanged =
                !string.Equals(
                    originalWorkOrder ?? "",
                    entity.CurrentWorkOrder ?? "",
                    StringComparison.OrdinalIgnoreCase);

            var workOrderTypeChanged =
                !string.Equals(
                    NormalizeWorkOrderType(originalWorkOrderClass),
                    NormalizeWorkOrderType(entity.WorkOrderClass),
                    StringComparison.OrdinalIgnoreCase);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                if (IsColumnTooLongException(ex, "current_work_order"))
                    return BadRequest("Work Order is too long. Work Order is limited to 10 characters.");

                return BadRequest("Ticket could not be saved because one or more fields are too long for the database.");
            }

            if (workOrderChanged || workOrderTypeChanged)
            {
                await TrySendPublishedAssignmentTicketModifiedEmailAsync(
                    entity.Id,
                    "Work Order / WO Type changed",
                    new List<string>
                    {
                        workOrderChanged
                            ? $"Work Order: {BlankForDisplay(originalWorkOrder)} → {BlankForDisplay(entity.CurrentWorkOrder)}"
                            : "",

                        workOrderTypeChanged
                            ? $"WO Type: {BlankForDisplay(NormalizeWorkOrderType(originalWorkOrderClass))} → {BlankForDisplay(NormalizeWorkOrderType(entity.WorkOrderClass))}"
                            : ""
                    }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList(),
                    ct);
            }

            return Ok(new UpdateTicketResponse(entity.Id));
        }

        [HttpPost("{id:long}/delete")]
        public async Task<ActionResult<DeleteTicketResponse>> DeleteTicket(long id,[FromBody] DeleteTicketRequest? req, CancellationToken ct)
        {
            req ??= new DeleteTicketRequest();

            if (!req.ConfirmPermanentDelete)
            {
                return BadRequest(
                    "Permanent-delete confirmation is required.");
            }

            var deletedBy =
                string.IsNullOrWhiteSpace(req.DeletedBy)
                    ? "Unknown"
                    : req.DeletedBy.Trim();

            await using var transaction =
                await _db.Database.BeginTransactionAsync(ct);

            try
            {
                var ticket = await _db.Tickets
                    .FirstOrDefaultAsync(
                        x => x.Id == id,
                        ct);

                if (ticket == null)
                    return NotFound("Ticket was not found.");

                var ticketSite =
                    (ticket.Site ?? string.Empty).Trim();

                var ticketNotification =
                    (ticket.Notification ?? string.Empty).Trim();

                /*
                 * Site History is permanent operational history and is intentionally
                 * preserved when its originating ticket is deleted.
                 */
                var submissionRows =
                    await _db.TicketWriteUpSubmissions
                        .AsNoTracking()
                        .Where(x => x.TicketId == id)
                        .Select(x => new
                        {
                            x.Id,
                            x.SiteHistoryId
                        })
                        .ToListAsync(ct);

                var submissionIds =
                    submissionRows
                        .Select(x => x.Id)
                        .ToList();

                var preservedSiteHistoryCount =
                    submissionRows
                        .Where(x => x.SiteHistoryId.HasValue)
                        .Select(x => x.SiteHistoryId!.Value)
                        .Distinct()
                        .Count();

                var writeUpParticipantCount = 0;

                if (submissionIds.Count > 0)
                {
                    writeUpParticipantCount =
                        await _db.TicketWriteUpSubmissionTechnicians
                            .Where(x =>
                                submissionIds.Contains(
                                    x.SubmissionId))
                            .ExecuteDeleteAsync(ct);
                }

                var writeUpSubmissionCount =
                    await _db.TicketWriteUpSubmissions
                        .Where(x => x.TicketId == id)
                        .ExecuteDeleteAsync(ct);

                /*
                 * Published rows reference draft assignments through SourceAssignmentId,
                 * so published rows are removed first.
                 */
                var publishedAssignmentCount =
                    await _db.DailyTicketAssignmentPublished
                        .Where(x => x.TicketId == id)
                        .ExecuteDeleteAsync(ct);

                var draftAssignmentCount =
                    await _db.DailyTicketAssignments
                        .Where(x => x.TicketId == id)
                        .ExecuteDeleteAsync(ct);

                _db.Tickets.Remove(ticket);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                _logger.LogWarning(
                    "Ticket {TicketId} for site {Site} was permanently deleted by {DeletedBy}. " +
                    "Draft assignments: {DraftAssignments}; published assignments: {PublishedAssignments}; " +
                    "write-up submissions: {WriteUpSubmissions}; participants: {Participants}; " +
                    "preserved Site History rows: {SiteHistoryRows}.",
                    id,
                    ticketSite,
                    deletedBy,
                    draftAssignmentCount,
                    publishedAssignmentCount,
                    writeUpSubmissionCount,
                    writeUpParticipantCount,
                    preservedSiteHistoryCount);

                return Ok(
                    new DeleteTicketResponse
                    {
                        TicketId = id,
                        Site = ticketSite,
                        Notification = ticketNotification,

                        DraftAssignmentCount =
                            draftAssignmentCount,

                        PublishedAssignmentCount =
                            publishedAssignmentCount,

                        WriteUpSubmissionCount =
                            writeUpSubmissionCount,

                        WriteUpParticipantCount =
                            writeUpParticipantCount,

                        PreservedSiteHistoryCount =
                            preservedSiteHistoryCount
                    });
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);

                _logger.LogError(
                    ex,
                    "Ticket {TicketId} could not be permanently deleted by {DeletedBy}.",
                    id,
                    deletedBy);

                return Conflict(
                    "The ticket could not be deleted because another database record " +
                    "still references it. No records were deleted.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);

                _logger.LogError(
                    ex,
                    "Unexpected failure permanently deleting ticket {TicketId} by {DeletedBy}.",
                    id,
                    deletedBy);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "The ticket could not be deleted. No records were deleted.");
            }
        }

        [HttpPost("bulk-assign")]
        public async Task<ActionResult<BulkTicketUpdateResponse>> BulkAssignTickets([FromBody] BulkAssignTicketsRequest req,
            CancellationToken ct)
        {
            req ??= new BulkAssignTicketsRequest();

            var ticketIds = (req.TicketIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return BadRequest("At least one ticket is required.");

            var assignedTech =
                string.IsNullOrWhiteSpace(req.AssignedTech)
                    ? "(Unassigned)"
                    : req.AssignedTech.Trim();

            var isUnassigning =
                assignedTech.Equals(
                    "(Unassigned)",
                    StringComparison.OrdinalIgnoreCase);

            var requestedStatus =
                isUnassigning
                    ? "Open"
                    : "Assigned";

            var requestedStatusLower =
                requestedStatus.ToLower();

            var statusEntity = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.IsActive &&
                        x.Name.ToLower() == requestedStatusLower,
                    ct);

            if (statusEntity == null)
            {
                return BadRequest(
                    $"Status '{requestedStatus}' is missing or inactive.");
            }

            var now = DateTime.Now;

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var ticket in tickets)
            {
                ticket.AssignedTech = assignedTech;
                ticket.Status = statusEntity.Name;
                ticket.LastActivityAt = now;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new BulkTicketUpdateResponse
            {
                RequestedCount = ticketIds.Count,
                UpdatedCount = tickets.Count,
                NotFoundCount = ticketIds.Count - tickets.Count
            });
        }

        [HttpPost("bulk-set-problem")]
        public async Task<ActionResult<BulkTicketUpdateResponse>> BulkSetProblem([FromBody] BulkSetProblemRequest req, CancellationToken ct)
        {
            req ??= new BulkSetProblemRequest();

            var ticketIds = (req.TicketIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return BadRequest("At least one ticket is required.");

            var problem = (req.Problem ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(problem))
                return BadRequest("Problem / Issue is required.");

            var now = DateTime.Now;

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var ticket in tickets)
            {
                ticket.Problem = problem;
                ticket.Summary = FirstNonBlank(problem, ticket.NotificationName);
                ticket.LastActivityAt = now;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new BulkTicketUpdateResponse
            {
                RequestedCount = ticketIds.Count,
                UpdatedCount = tickets.Count,
                NotFoundCount = ticketIds.Count - tickets.Count
            });
        }

        [HttpPost("bulk-set-status")]
        public async Task<ActionResult<BulkTicketUpdateResponse>> BulkSetStatus([FromBody] BulkSetStatusRequest req,
            CancellationToken ct)
        {
            req ??= new BulkSetStatusRequest();

            var ticketIds = (req.TicketIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return BadRequest("At least one ticket is required.");

            var requestedStatus = (req.Status ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(requestedStatus))
                return BadRequest("Status is required.");

            var requestedStatusLower = requestedStatus.ToLower();

            var statusEntity = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.IsActive &&
                        x.Name.ToLower() == requestedStatusLower,
                    ct);

            if (statusEntity == null)
                return BadRequest("Selected status is invalid or inactive.");

            var now = DateTime.Now;

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var ticket in tickets)
            {
                ticket.Status = statusEntity.Name;
                ticket.LastActivityAt = now;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new BulkTicketUpdateResponse
            {
                RequestedCount = ticketIds.Count,
                UpdatedCount = tickets.Count,
                NotFoundCount = ticketIds.Count - tickets.Count
            });
        }

        [HttpPost("bulk-set-work-order-type")]
        public async Task<ActionResult<BulkTicketUpdateResponse>> BulkSetWorkOrderType([FromBody] BulkSetWorkOrderTypeRequest req,
            CancellationToken ct)
        {
            req ??= new BulkSetWorkOrderTypeRequest();

            var ticketIds = (req.TicketIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (ticketIds.Count == 0)
                return BadRequest("At least one ticket is required.");

            var requestedType = (req.WorkOrderType ?? string.Empty).Trim();

            string? storedType = null;

            if (!string.IsNullOrWhiteSpace(requestedType))
            {
                storedType = NormalizeWorkOrderClassForStorage(requestedType);

                if (string.IsNullOrWhiteSpace(storedType))
                    return BadRequest("Work Order Type must be blank, Maintenance, Capital, or Distribution.");
            }

            var now = DateTime.Now;

            var tickets = await _db.Tickets
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct);

            var updated = 0;
            var skipped = 0;

            foreach (var ticket in tickets)
            {
                /*
                 * Keep the existing data rule: WO Type only matters when a Work Order
                 * exists. Tickets without a WO are skipped instead of creating dirty data.
                 */
                if (string.IsNullOrWhiteSpace(ticket.CurrentWorkOrder))
                {
                    skipped++;
                    continue;
                }

                ticket.WorkOrderClass = storedType;
                ticket.LastActivityAt = now;
                updated++;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new BulkTicketUpdateResponse
            {
                RequestedCount = ticketIds.Count,
                UpdatedCount = updated,
                NotFoundCount = ticketIds.Count - tickets.Count,
                SkippedCount = skipped
            });
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
        public async Task<ActionResult<SapQueueImportCommitResponse>> CommitSapImport([FromBody] SapQueueImportCommitRequest req, CancellationToken ct)
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
                    await _db.SaveChangesAsync(ct);

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

            await RecordSapImportRunAsync(
                importTime,
                createdBy,
                imported,
                alreadyExists,
                invalid,
                ct);

            return Ok(new SapQueueImportCommitResponse(
                ImportedCount: imported,
                AlreadyExistsCount: alreadyExists,
                InvalidCount: invalid,
                Rows: results));
        }

        [HttpGet("sap-import/last")]
        public async Task<ActionResult<SapQueueImportLastImportDto>> GetLastSapImport(
            CancellationToken ct)
        {
            var rows = await _db.Database
                .SqlQueryRaw<SapQueueImportLastImportDto>(
                    """
                    SELECT
                        imported_at AS ImportedAt,
                        imported_by AS ImportedBy,
                        imported_count AS ImportedCount
                    FROM sap_queue_import_runs
                    ORDER BY id DESC
                    LIMIT 1
                    """)
                .ToListAsync(ct);

            return Ok(
                rows.FirstOrDefault()
                ?? new SapQueueImportLastImportDto());
        }

        private async Task RecordSapImportRunAsync(DateTime importedAt, string importedBy, int importedCount, int alreadyExistsCount,
            int invalidCount, CancellationToken ct)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                INSERT INTO sap_queue_import_runs
                    (imported_at, imported_by, imported_count, already_exists_count, invalid_count)
                VALUES
                    ({importedAt}, {importedBy}, {importedCount}, {alreadyExistsCount}, {invalidCount})
                """,
                ct);
        }

        private static string BlankForDisplay(string? value)
        {
            var clean = (value ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(clean)
                ? "—"
                : clean;
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

        private static string? ValidateCreateTicketRequest(CreateTicketRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Site))
                return TicketSiteRequiredMessage;

            return
                ValidateTicketTextLength("Site", req.Site, TicketTextLimits.Site) ??
                ValidateTicketTextLength("Notification Name", req.NotificationName, TicketTextLimits.NotificationName) ??
                ValidateTicketTextLength("Notification #", req.Notification, TicketTextLimits.Notification) ??
                ValidateTicketTextLength("Work Order", req.WorkOrder, TicketTextLimits.WorkOrder) ??
                ValidateTicketTextLength("Work Order Type", req.WorkOrderClass, TicketTextLimits.WorkOrderClass) ??
                ValidateTicketTextLength("Work Order Code", req.GroupCode, TicketTextLimits.GroupCode) ??
                ValidateTicketTextLength("Status", req.Status, TicketTextLimits.Status) ??
                ValidateTicketTextLength("Assigned Tech", req.AssignedTech, TicketTextLimits.AssignedTech) ??
                ValidateTicketTextLength("Problem", req.Problem, TicketTextLimits.Problem) ??
                ValidateTicketTextLength("Notes", req.Notes, TicketTextLimits.Notes) ??
                ValidateTicketTextLength("Dispatch Notes", req.DispatchNotes, TicketTextLimits.DispatchNotes) ??
                ValidateTicketTextLength("Created By", req.CreatedBy, TicketTextLimits.CreatedBy);
        }

        private static string? ValidateUpdateTicketRequest(UpdateTicketRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Site))
                return TicketSiteRequiredMessage;

            return
                ValidateTicketTextLength("Site", req.Site, TicketTextLimits.Site) ??
                ValidateTicketTextLength("Notification Name", req.NotificationName, TicketTextLimits.NotificationName) ??
                ValidateTicketTextLength("Notification #", req.Notification, TicketTextLimits.Notification) ??
                ValidateTicketTextLength("Work Order", req.WorkOrder, TicketTextLimits.WorkOrder) ??
                ValidateTicketTextLength("Work Order Type", req.WorkOrderClass, TicketTextLimits.WorkOrderClass) ??
                ValidateTicketTextLength("Work Order Code", req.GroupCode, TicketTextLimits.GroupCode) ??
                ValidateTicketTextLength("Status", req.Status, TicketTextLimits.Status) ??
                ValidateTicketTextLength("Assigned Tech", req.AssignedTech, TicketTextLimits.AssignedTech) ??
                ValidateTicketTextLength("Problem", req.Problem, TicketTextLimits.Problem) ??
                ValidateTicketTextLength("Notes", req.Notes, TicketTextLimits.Notes) ??
                ValidateTicketTextLength("Dispatch Notes", req.DispatchNotes, TicketTextLimits.DispatchNotes);
        }

        private static bool IsColumnTooLongException(DbUpdateException ex, string columnName)
        {
            var message = ex.InnerException?.Message ?? ex.Message;

            return message.Contains("Data too long for column", StringComparison.OrdinalIgnoreCase) &&
                   message.Contains(columnName, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ValidateTicketTextLength(string fieldName, string? value, int maxLength)
        {
            var length = (value ?? string.Empty).Trim().Length;

            if (length <= maxLength)
                return null;

            return $"{fieldName} is limited to {maxLength} characters. Current length is {length} characters.";
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

        // Submits a technician write-up as one atomic operation so ticket state,
        // Site History, and technician completion History can never drift apart.
        [HttpPost("{id:long}/submit-writeup")]
        public async Task<ActionResult<UpdateTicketResponse>> SubmitWriteUp(long id, [FromBody] SubmitTicketWriteUpRequest req,
            CancellationToken ct)
        {
            if (req.ClientSubmissionId == Guid.Empty)
            {
                return BadRequest(
                    "ClientSubmissionId is required.");
            }

            var clientSubmissionId =
                req.ClientSubmissionId;

            /*
             * A repeated request with the same client ID represents the same confirmed
             * write-up. Return success without modifying the ticket or Site History again.
             */
            var existingSubmission =
                await _db.TicketWriteUpSubmissions
                    .AsNoTracking()
                    .Where(x =>
                        x.ClientSubmissionId ==
                        clientSubmissionId)
                    .Select(x => new
                    {
                        x.TicketId
                    })
                    .FirstOrDefaultAsync(ct);

            if (existingSubmission != null)
            {
                if (existingSubmission.TicketId != id)
                {
                    return Conflict(
                        "This client submission ID is already associated with another ticket.");
                }

                return Ok(
                    new UpdateTicketResponse(id));
            }


            var entity = await _db.Tickets
                .FirstOrDefaultAsync(t => t.Id == id, ct);

            if (entity == null)
                return NotFound();

            var finalWriteUp = (req.FinalWriteUpText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(finalWriteUp))
                return BadRequest("Write-up text is required.");

            var siteHistoryWriteUp = string.IsNullOrWhiteSpace(req.SiteHistoryWriteUpText)
                ? finalWriteUp
                : req.SiteHistoryWriteUpText.Trim();

            var writeUpSubmitStatus = await _db.TicketStatuses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.IsActive && x.IsWriteUpSubmitTarget,
                    ct);

            if (writeUpSubmitStatus == null)
            {
                return BadRequest(
                    "No active ticket status is configured for submitted write-ups. " +
                    "Go to Administration > Tickets and select one status as the write-up submitted target.");
            }

            var submittedAt = DateTime.Now;

            await using var transaction = await _db.Database
                .BeginTransactionAsync(ct);

            try
            {
                var submittedWork = await ResolveSubmittedWorkAsync(
                    entity,
                    req.SubmittedBy,
                    submittedAt.Date,
                    ct);

                /*
                 * The API owns the final technician footer. This ensures Ticket Notes,
                 * the structured submission, Site History, Field Tech History, and email
                 * all use the same authoritative participant list.
                 */
                var canonicalFinalWriteUp = ApplyCnpTechFooter(
                    finalWriteUp,
                    submittedWork);

                var canonicalSiteHistoryWriteUp = ApplyCnpTechFooter(
                    siteHistoryWriteUp,
                    submittedWork);

                entity.Notes = AppendTicketNote(
                    entity.Notes,
                    "Write-up submitted",
                    canonicalFinalWriteUp,
                    req.SubmittedBy);

                entity.ActionRequiredOverride =
                    "Review submitted site write-up";

                entity.LastActivityAt = submittedAt;
                entity.Status = writeUpSubmitStatus.Name;

                await CreateSubmittedWriteUpRecordsAsync(
                    entity,
                    canonicalSiteHistoryWriteUp,
                    submittedWork,
                    clientSubmissionId,
                    submittedAt,
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                await TrySendWriteUpSubmittedEmailAsync(
                    entity.Id,
                    canonicalFinalWriteUp,
                    submittedAt,
                    submittedWork,
                    ct);

                return Ok(new UpdateTicketResponse(entity.Id));
            }
            catch (DbUpdateException ex)
                when (IsDuplicateClientSubmissionIdException(ex))
            {
                await transaction.RollbackAsync(ct);

                /*
                 * Remove rolled-back tracked entities before checking the record created
                 * by the competing or earlier request.
                 */
                _db.ChangeTracker.Clear();

                var completedSubmission =
                    await _db.TicketWriteUpSubmissions
                        .AsNoTracking()
                        .Where(x =>
                            x.ClientSubmissionId ==
                            clientSubmissionId)
                        .Select(x => new
                        {
                            x.TicketId
                        })
                        .FirstOrDefaultAsync(ct);

                if (completedSubmission?.TicketId == id)
                {
                    return Ok(
                        new UpdateTicketResponse(id));
                }

                throw;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        // Creates Site History, the structured write-up submission, and permanent
        // participant rows using the client idempotency key for duplicate protection.
        private async Task CreateSubmittedWriteUpRecordsAsync(
            TicketEntity ticket,
            string siteHistoryWriteUp,
            SubmittedWorkInfo submittedWork,
            Guid clientSubmissionId,
            DateTime submittedAt,
            CancellationToken ct)
        {
            long? siteHistoryId = null;

            var siteId = (ticket.Site ?? string.Empty).Trim();

            /*
             * Site History requires a site ID. If an unusual ticket has no site,
             * still record the structured submission and its participants.
             */
            if (!string.IsNullOrWhiteSpace(siteId))
            {
                var siteHistory = new SiteHistoryEntity
                {
                    LegacySourceId = null,
                    SourceType = "SmartGridSuite",
                    SourceFile = $"Ticket {ticket.Id}",
                    SiteId = siteId,
                    VisitDate = submittedAt.Date,

                    PrimaryTech = TrimForColumn(
                        submittedWork.PrimaryTech,
                        100),

                    SecondaryTech = TrimForColumn(
                        submittedWork.SecondaryTech,
                        100),

                    Narrative = siteHistoryWriteUp,
                    IssueText = ticket.Problem ?? string.Empty
                };

                _db.SiteHistory.Add(siteHistory);

                /*
                 * Save inside the transaction so MySQL generates HistoryId before
                 * the linked structured submission is created.
                 */
                await _db.SaveChangesAsync(ct);

                siteHistoryId = siteHistory.HistoryId;
            }

            var submission = new TicketWriteUpSubmissionEntity
            {
                TicketId = ticket.Id,
                SiteHistoryId = siteHistoryId,
                ClientSubmissionId = clientSubmissionId,

                SubmittedByTechnicianId =
                    submittedWork.SubmittedByTechnicianId,

                SubmittedByEmployeeId =
                    TrimForColumn(
                        submittedWork.SubmittedByEmployeeId,
                        100)
                    ?? "Unknown",

                SubmittedByName =
                    TrimForColumn(
                        submittedWork.SubmittedByName,
                        150)
                    ?? "Unknown",

                SubmittedAt = submittedAt,
                SubmittedNarrative = siteHistoryWriteUp,

                IsDeleted = false,
                DeletedAt = null,
                DeletedBy = null
            };

            _db.TicketWriteUpSubmissions.Add(submission);

            /*
             * Generate SubmissionId before creating participant rows. This save remains
             * inside SubmitWriteUp's transaction, so a later failure still rolls back.
             */
            await _db.SaveChangesAsync(ct);

            var participantRows = submittedWork.Participants
                .Select(participant =>
                    new TicketWriteUpSubmissionTechnicianEntity
                    {
                        SubmissionId = submission.Id,

                        TechnicianId = participant.TechnicianId,

                        EmployeeId =
                            TrimForColumn(participant.EmployeeId, 100)
                            ?? "Unknown",

                        TechnicianName =
                            TrimForColumn(participant.TechnicianName, 150)
                            ?? "Unknown",

                        IsSubmitter = participant.IsSubmitter,
                        CreatedAt = submittedAt
                    })
                .ToList();

            _db.TicketWriteUpSubmissionTechnicians.AddRange(
                participantRows);
        }

        // Resolves everyone assigned to the ticket at submission time. Published
        // assignment ownership is preferred over mutable display text such as AssignedTech.
        private async Task<SubmittedWorkInfo> ResolveSubmittedWorkAsync(TicketEntity ticket, string? submittedByEmployeeId,
            DateTime submittedDate, CancellationToken ct)
        {
            var submittedEmployeeId =
                (submittedByEmployeeId ?? string.Empty).Trim();

            var participants =
                new Dictionary<string, SubmittedParticipantInfo>(
                    StringComparer.OrdinalIgnoreCase);

            var submitter = string.IsNullOrWhiteSpace(submittedEmployeeId)
                ? null
                : await _db.Technicians
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.EmployeeId == submittedEmployeeId,
                        ct);

            /*
             * Prefer the newest published assignment because that represents the route
             * the field technicians actually received, even if Dispatch has since begun
             * making unpublished draft changes.
             */
            var publishedTarget = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x => x.TicketId == ticket.Id)
                .OrderByDescending(x => x.PublishedAt)
                .ThenByDescending(x => x.PublishedVersion)
                .ThenByDescending(x => x.Id)
                .Select(x => new WriteUpAssignmentTargetInfo
                {
                    TargetType = x.TargetType,
                    TruckId = x.TruckId,
                    TechnicianId = x.TechnicianId
                })
                .FirstOrDefaultAsync(ct);

            /*
             * A directly assigned ticket may not have a published snapshot. Fall back
             * to the current active assignment record when necessary.
             */
            if (publishedTarget == null)
            {
                publishedTarget = await _db.DailyTicketAssignments
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == ActiveAssignmentDate &&
                        x.TicketId == ticket.Id)
                    .OrderByDescending(x => x.IsPublished)
                    .ThenByDescending(x => x.UpdatedAt)
                    .ThenByDescending(x => x.Id)
                    .Select(x => new WriteUpAssignmentTargetInfo
                    {
                        TargetType = x.TargetType,
                        TruckId = x.TruckId,
                        TechnicianId = x.TechnicianId
                    })
                    .FirstOrDefaultAsync(ct);
            }

            /*
             * A technician-owned assignment carrying TruckId represents a crew route.
             * Snapshot everybody rostered to that truck on the submission date.
             */
            if (publishedTarget?.TruckId is uint assignedTruckId)
            {
                var assignedCrew = await (
                    from roster in _db.TruckRosters.AsNoTracking()
                    join tech in _db.Technicians.AsNoTracking()
                        on roster.TechnicianId equals tech.Id
                    where roster.WorkDate == submittedDate.Date
                          && roster.TruckId == assignedTruckId
                          && tech.IsActive
                          && tech.TechnicianRoles.Any(
                              role => role.Role.Code == TechnicianRoleCode)
                    select tech)
                    .ToListAsync(ct);

                foreach (var technician in assignedCrew)
                {
                    AddSubmittedParticipant(
                        participants,
                        technician,
                        isSubmitter: false);
                }
            }

            /*
             * A Technician target without truck context is personal work. Add that
             * assigned technician directly.
             */
            if (participants.Count == 0 &&
                publishedTarget?.TechnicianId is uint assignedTechnicianId)
            {
                var assignedTechnician = await _db.Technicians
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Id == assignedTechnicianId &&
                             x.IsActive,
                        ct);

                if (assignedTechnician != null)
                {
                    AddSubmittedParticipant(
                        participants,
                        assignedTechnician,
                        isSubmitter: false);
                }
            }

            /*
             * Legacy/direct AssignedTech values are a final fallback for tickets that
             * were assigned without a Daily Assignments record.
             */
            if (participants.Count == 0)
            {
                var assignedTechNames =
                    ParseAssignedTechnicianDisplayNames(
                        ticket.AssignedTech);

                if (assignedTechNames.Count > 0)
                {
                    var activeTechnicians = await ActiveFieldTechniciansQuery()
                        .ToListAsync(ct);

                    foreach (var technician in activeTechnicians)
                    {
                        var technicianName = FormatTechnicianName(
                            technician.FirstName,
                            technician.LastName,
                            technician.EmployeeId);

                        if (!assignedTechNames.Contains(technicianName) &&
                            !assignedTechNames.Contains(
                                technician.EmployeeId ?? string.Empty))
                        {
                            continue;
                        }

                        AddSubmittedParticipant(
                            participants,
                            technician,
                            isSubmitter: false);
                    }
                }
            }

            /*
             * The submitter is always included even if assignment data is incomplete.
             * If already present as a crew member, the existing participant is marked
             * as the submitter rather than inserted twice.
             */
            if (submitter != null)
            {
                AddSubmittedParticipant(
                    participants,
                    submitter,
                    isSubmitter: true);
            }
            else
            {
                var fallbackEmployeeId =
                    string.IsNullOrWhiteSpace(submittedEmployeeId)
                        ? "Unknown"
                        : submittedEmployeeId;

                AddSubmittedParticipant(
                    participants,
                    technicianId: null,
                    employeeId: fallbackEmployeeId,
                    technicianName: fallbackEmployeeId,
                    isSubmitter: true);
            }

            var submitterParticipant = participants.Values
                .FirstOrDefault(x => x.IsSubmitter);

            var submittedByName =
                submitterParticipant?.TechnicianName
                ?? "Unknown";

            var secondaryNames = participants.Values
                .Where(x => !x.IsSubmitter)
                .Select(x => x.TechnicianName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            return new SubmittedWorkInfo
            {
                SubmittedByTechnicianId =
                    submitterParticipant?.TechnicianId,

                SubmittedByEmployeeId =
                    submitterParticipant?.EmployeeId
                    ?? "Unknown",

                SubmittedByName = submittedByName,

                PrimaryTech = submittedByName,

                SecondaryTech = secondaryNames.Count == 0
                    ? null
                    : FormatCrewDisplayText(secondaryNames),

                Participants = participants.Values
                    .OrderByDescending(x => x.IsSubmitter)
                    .ThenBy(x => x.TechnicianName)
                    .ToList()
            };
        }

        // Adds a technician to the participant snapshot while preventing duplicate
        // employee entries and preserving the submitter flag.
        // Adds a technician to the participant snapshot while preventing duplicate
        // employee entries and preserving the submitter flag.
        private static void AddSubmittedParticipant(
            IDictionary<string, SubmittedParticipantInfo> participants,
            TechnicianEntity technician,
            bool isSubmitter)
        {
            var employeeId =
                string.IsNullOrWhiteSpace(technician.EmployeeId)
                    ? $"TECH-{technician.Id}"
                    : technician.EmployeeId.Trim();

            var technicianName = FormatTechnicianName(
                technician.FirstName,
                technician.LastName,
                employeeId);

            AddSubmittedParticipant(
                participants,
                technician.Id,
                employeeId,
                technicianName,
                isSubmitter,
                technician.EmailAddress);
        }

        // Adds or updates one participant using employee ID as the stable snapshot key.
        private static void AddSubmittedParticipant(
            IDictionary<string, SubmittedParticipantInfo> participants,
            uint? technicianId,
            string employeeId,
            string technicianName,
            bool isSubmitter,
            string? emailAddress = null)
        {
            var cleanEmployeeId =
                string.IsNullOrWhiteSpace(employeeId)
                    ? "Unknown"
                    : employeeId.Trim();

            var cleanTechnicianName =
                string.IsNullOrWhiteSpace(technicianName)
                    ? cleanEmployeeId
                    : technicianName.Trim();

            var cleanEmailAddress =
                (emailAddress ?? string.Empty).Trim();

            if (participants.TryGetValue(
                    cleanEmployeeId,
                    out var existing))
            {
                if (!existing.TechnicianId.HasValue &&
                    technicianId.HasValue)
                {
                    existing.TechnicianId = technicianId;
                }

                if (string.IsNullOrWhiteSpace(existing.EmailAddress) &&
                    !string.IsNullOrWhiteSpace(cleanEmailAddress))
                {
                    existing.EmailAddress = cleanEmailAddress;
                }

                if (isSubmitter)
                    existing.IsSubmitter = true;

                return;
            }

            participants[cleanEmployeeId] =
                new SubmittedParticipantInfo
                {
                    TechnicianId = technicianId,
                    EmployeeId = cleanEmployeeId,
                    TechnicianName = cleanTechnicianName,
                    EmailAddress = cleanEmailAddress,
                    IsSubmitter = isSubmitter
                };
        }

        // Splits the AssignedTech display value used for direct or older assignments.
        // Crew strings such as "Alex Smith, Pat Jones & Lee Brown" become exact names.
        private static HashSet<string> ParseAssignedTechnicianDisplayNames(
            string? assignedTech)
        {
            var value = (assignedTech ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals(
                    "(Unassigned)",
                    StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(
                    "Truck ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            }

            return Regex
                .Split(value, @"\s*(?:,|&)\s*")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        [HttpGet("site-history/{siteId}")]
        public async Task<ActionResult<List<SiteHistoryPreviewDto>>> GetSiteHistoryForSite(string siteId, CancellationToken ct)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return Ok(new List<SiteHistoryPreviewDto>());

            var matchKeys = BuildTicketSiteHistoryMatchKeys(siteId);

            if (matchKeys.Count == 0)
                return Ok(new List<SiteHistoryPreviewDto>());

            var historyRows = await _db.SiteHistory
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .ToListAsync(ct);

            var matchingHistoryRows = historyRows
                .Where(x => matchKeys.Contains(NormalizeTicketSiteHistoryKey(x.SiteId)))
                .OrderByDescending(x => x.VisitDate ?? DateTime.MinValue)
                .ThenByDescending(x => x.HistoryId)
                .ToList();

            if (matchingHistoryRows.Count == 0)
                return Ok(new List<SiteHistoryPreviewDto>());

            var historyIds = matchingHistoryRows
                .Select(x => x.HistoryId)
                .Distinct()
                .ToList();

            var submissionsByHistoryId = await _db.TicketWriteUpSubmissions
                .AsNoTracking()
                .Where(x =>
                    !x.IsDeleted &&
                    x.SiteHistoryId.HasValue &&
                    historyIds.Contains(x.SiteHistoryId.Value))
                .GroupBy(x => x.SiteHistoryId!.Value)
                .Select(g => new
                {
                    SiteHistoryId = g.Key,
                    SubmissionId = g.Min(x => x.Id),
                    SubmittedAt = g.Min(x => x.SubmittedAt)
                })
                .ToDictionaryAsync(
                    x => x.SiteHistoryId,
                    x => new
                    {
                        x.SubmissionId,
                        x.SubmittedAt
                    },
                    ct);

            var result = matchingHistoryRows
                .Select(x =>
                {
                    var submissionInfo = submissionsByHistoryId.TryGetValue(
                        x.HistoryId,
                        out var foundSubmission)
                            ? foundSubmission
                            : null;

                    return new SiteHistoryPreviewDto
                    {
                        HistoryId = x.HistoryId,
                        SubmissionId = submissionInfo?.SubmissionId,

                        SiteId = x.SiteId ?? "",
                        SourceType = x.SourceType ?? "",

                        VisitDate = x.VisitDate,
                        SubmittedAt = submissionInfo?.SubmittedAt,

                        PrimaryTech = x.PrimaryTech,
                        SecondaryTech = x.SecondaryTech,
                        IssueText = x.IssueText,
                        Narrative = x.Narrative,

                        EditedAt = x.EditedAt,
                        EditedBy = x.EditedBy
                    };
                })
                .ToList();

            return Ok(result);
        }

        private static List<string> BuildTicketSiteHistoryMatchKeys(params string?[] values)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in values)
            {
                var key = NormalizeTicketSiteHistoryKey(value);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                keys.Add(key);

                if (key.EndsWith("MR", StringComparison.OrdinalIgnoreCase) &&
                    key.Length > 2)
                {
                    keys.Add(key[..^2]);
                }
            }

            return keys.ToList();
        }

        private static string NormalizeTicketSiteHistoryKey(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Replace(".", "")
                .ToUpperInvariant();
        }

        [HttpPost("writeups/{submissionId:long}/update")]
        public async Task<ActionResult<SubmittedWriteUpMutationResponse>> UpdateSubmittedWriteUp(long submissionId,
            [FromBody] UpdateSubmittedWriteUpRequest req, CancellationToken ct)
        {
            req ??= new UpdateSubmittedWriteUpRequest();

            var narrative = (req.Narrative ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(narrative))
                return BadRequest("Write-up text is required.");

            var updatedBy =
                string.IsNullOrWhiteSpace(req.UpdatedBy)
                    ? "Unknown"
                    : req.UpdatedBy.Trim();

            var now = DateTime.Now;

            await using var transaction =
                await _db.Database.BeginTransactionAsync(ct);

            var submission = await _db.TicketWriteUpSubmissions
                .FirstOrDefaultAsync(x => x.Id == submissionId, ct);

            if (submission == null)
                return NotFound();

            if (submission.IsDeleted)
                return BadRequest("This write-up has been deleted and cannot be edited.");

            submission.SubmittedNarrative = narrative;
            submission.EditedAt = now;
            submission.EditedBy = TrimForColumn(updatedBy, 100);

            if (submission.SiteHistoryId.HasValue)
            {
                var siteHistory = await _db.SiteHistory
                    .FirstOrDefaultAsync(
                        x => x.HistoryId == submission.SiteHistoryId.Value,
                        ct);

                if (siteHistory != null)
                {
                    var originalPrimaryTech = siteHistory.PrimaryTech;
                    var originalSecondaryTech = siteHistory.SecondaryTech;

                    siteHistory.Narrative = narrative;

                    if (req.IssueText != null)
                    {
                        siteHistory.IssueText =
                            NormalizeEditableSiteHistoryText(req.IssueText);
                    }

                    if (req.PrimaryTech != null)
                    {
                        siteHistory.PrimaryTech = TrimForColumn(
                            NormalizeEditableSiteHistoryText(req.PrimaryTech),
                            100);
                    }

                    if (req.SecondaryTech != null)
                    {
                        siteHistory.SecondaryTech = TrimForColumn(
                            NormalizeEditableSiteHistoryText(req.SecondaryTech),
                            100);
                    }

                    siteHistory.EditedAt = now;
                    siteHistory.EditedBy = TrimForColumn(updatedBy, 100);

                    var technicianOwnershipChanged =
                        !string.Equals(
                            NormalizeComparableSiteHistoryText(originalPrimaryTech),
                            NormalizeComparableSiteHistoryText(siteHistory.PrimaryTech),
                            StringComparison.OrdinalIgnoreCase) ||

                        !string.Equals(
                            NormalizeComparableSiteHistoryText(originalSecondaryTech),
                            NormalizeComparableSiteHistoryText(siteHistory.SecondaryTech),
                            StringComparison.OrdinalIgnoreCase);

                    if (technicianOwnershipChanged)
                    {
                        var participantRewriteError =
                            await RewriteSubmittedWriteUpParticipantsAsync(
                                submission,
                                siteHistory.PrimaryTech,
                                siteHistory.SecondaryTech,
                                now,
                                ct);

                        if (!string.IsNullOrWhiteSpace(participantRewriteError))
                            return BadRequest(participantRewriteError);
                    }
                }
            }

            var ticket = await _db.Tickets
                .FirstOrDefaultAsync(x => x.Id == submission.TicketId, ct);

            if (ticket != null)
            {
                ticket.LastActivityAt = now;

                ticket.DispatchNotes = AppendTicketNote(
                    ticket.DispatchNotes,
                    "Write-up edited",
                    $"Site History write-up #{submission.Id} was edited by Dispatch.",
                    updatedBy);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Ok(new SubmittedWriteUpMutationResponse
            {
                SubmissionId = submission.Id,
                SiteHistoryId = submission.SiteHistoryId,
                TicketId = submission.TicketId,
                ChangedAt = now
            });
        }

        [HttpPost("writeups/{submissionId:long}/delete")]
        public async Task<ActionResult<SubmittedWriteUpMutationResponse>> DeleteSubmittedWriteUp(long submissionId,
            [FromBody] DeleteSubmittedWriteUpRequest req, CancellationToken ct)
        {
            req ??= new DeleteSubmittedWriteUpRequest();

            var deletedBy =
                string.IsNullOrWhiteSpace(req.DeletedBy)
                    ? "Unknown"
                    : req.DeletedBy.Trim();

            var now = DateTime.Now;

            await using var transaction =
                await _db.Database.BeginTransactionAsync(ct);

            var submission = await _db.TicketWriteUpSubmissions
                .FirstOrDefaultAsync(x => x.Id == submissionId, ct);

            if (submission == null)
                return NotFound();

            if (!submission.IsDeleted)
            {
                submission.IsDeleted = true;
                submission.DeletedAt = now;
                submission.DeletedBy = TrimForColumn(deletedBy, 100);
            }

            if (submission.SiteHistoryId.HasValue)
            {
                var siteHistory = await _db.SiteHistory
                    .FirstOrDefaultAsync(
                        x => x.HistoryId == submission.SiteHistoryId.Value,
                        ct);

                if (siteHistory != null && !siteHistory.IsDeleted)
                {
                    siteHistory.IsDeleted = true;
                    siteHistory.DeletedAt = now;
                    siteHistory.DeletedBy = TrimForColumn(deletedBy, 100);
                }
            }

            var ticket = await _db.Tickets
                .FirstOrDefaultAsync(x => x.Id == submission.TicketId, ct);

            if (ticket != null)
            {
                ticket.LastActivityAt = now;

                ticket.DispatchNotes = AppendTicketNote(
                    ticket.DispatchNotes,
                    "Write-up deleted",
                    $"Site History write-up #{submission.Id} was deleted by Dispatch.",
                    deletedBy);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Ok(new SubmittedWriteUpMutationResponse
            {
                SubmissionId = submission.Id,
                SiteHistoryId = submission.SiteHistoryId,
                TicketId = submission.TicketId,
                ChangedAt = now
            });
        }

        // Carries submission identity, Site History display names, and every technician
        // who should receive this completed work in personal History.
        private sealed class SubmittedWorkInfo
        {
            public uint? SubmittedByTechnicianId { get; set; }

            public string SubmittedByEmployeeId { get; set; } = "";

            public string SubmittedByName { get; set; } = "";

            public string PrimaryTech { get; set; } = "";

            public string? SecondaryTech { get; set; }

            public List<SubmittedParticipantInfo> Participants { get; set; } = new();
        }

        // Represents one permanent technician participant snapshot for a submission.
        private sealed class SubmittedParticipantInfo
        {
            public uint? TechnicianId { get; set; }

            public string EmployeeId { get; set; } = "";

            public string TechnicianName { get; set; } = "";

            public string EmailAddress { get; set; } = "";

            public bool IsSubmitter { get; set; }
        }

        // Carries only the assignment ownership needed to resolve write-up participants.
        private sealed class WriteUpAssignmentTargetInfo
        {
            public string TargetType { get; set; } = "";

            public uint? TruckId { get; set; }

            public uint? TechnicianId { get; set; }
        }

        private static string ApplyCnpTechFooter(
            string? writeUp,
            SubmittedWorkInfo submittedWork)
        {
            var technicianNames = submittedWork.Participants
                .OrderByDescending(x => x.IsSubmitter)
                .ThenBy(x => x.TechnicianName)
                .Select(x => (x.TechnicianName ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var crewDisplayText = FormatCrewDisplayText(technicianNames);
            var cleanWriteUp = (writeUp ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(crewDisplayText))
                return cleanWriteUp;

            /*
             * Remove the client-generated CNP Techs footer so the API can replace it
             * with the authoritative participant list resolved at submission time.
             */
            cleanWriteUp = Regex.Replace(
                    cleanWriteUp,
                    @"(?:\r?\n){0,2}-{10,}\r?\nCNP Techs:\s*[^\r\n]*\s*$",
                    string.Empty,
                    RegexOptions.IgnoreCase)
                .TrimEnd();

            var footer =
                "----------------------------" +
                Environment.NewLine +
                $"CNP Techs: {crewDisplayText}";

            return string.IsNullOrWhiteSpace(cleanWriteUp)
                ? footer
                : cleanWriteUp +
                  Environment.NewLine +
                  Environment.NewLine +
                  footer;
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

        private async Task<string?> RewriteSubmittedWriteUpParticipantsAsync(
            TicketWriteUpSubmissionEntity submission, string? primaryTechText, string? secondaryTechText,
            DateTime changedAt, CancellationToken ct)
        {
            var activeTechnicians = await ActiveFieldTechniciansQuery()
                .ToListAsync(ct);

            var primaryText = NormalizeEditableSiteHistoryText(primaryTechText);
            var secondaryText = NormalizeEditableSiteHistoryText(secondaryTechText);

            var primaryTech = ResolveTechnicianForHistoryEdit(
                primaryText,
                activeTechnicians);

            var secondaryTech = ResolveTechnicianForHistoryEdit(
                secondaryText,
                activeTechnicians);

            if (!string.IsNullOrWhiteSpace(primaryText) && primaryTech == null)
            {
                return $"Primary Tech '{primaryText}' could not be matched to an active technician.";
            }

            if (!string.IsNullOrWhiteSpace(secondaryText) && secondaryTech == null)
            {
                return $"Secondary Tech '{secondaryText}' could not be matched to an active technician.";
            }

            if (primaryTech == null && secondaryTech == null)
            {
                return "At least one technician is required when changing write-up technician ownership.";
            }

            var existingParticipantRows = await _db.TicketWriteUpSubmissionTechnicians
                .Where(x => x.SubmissionId == submission.Id)
                .ToListAsync(ct);

            _db.TicketWriteUpSubmissionTechnicians.RemoveRange(existingParticipantRows);

            var participantRows =
                new List<TicketWriteUpSubmissionTechnicianEntity>();

            if (primaryTech != null)
            {
                var primaryName = FormatTechnicianName(
                    primaryTech.FirstName,
                    primaryTech.LastName,
                    primaryTech.EmployeeId);

                participantRows.Add(new TicketWriteUpSubmissionTechnicianEntity
                {
                    SubmissionId = submission.Id,
                    TechnicianId = primaryTech.Id,
                    EmployeeId = TrimForColumn(primaryTech.EmployeeId, 100) ?? "Unknown",
                    TechnicianName = TrimForColumn(primaryName, 150) ?? "Unknown",
                    IsSubmitter = true,
                    CreatedAt = changedAt
                });

                submission.SubmittedByTechnicianId = primaryTech.Id;
                submission.SubmittedByEmployeeId = TrimForColumn(primaryTech.EmployeeId, 100) ?? "Unknown";
                submission.SubmittedByName = TrimForColumn(primaryName, 150) ?? "Unknown";
            }

            if (secondaryTech != null &&
                secondaryTech.Id != primaryTech?.Id)
            {
                var secondaryName = FormatTechnicianName(
                    secondaryTech.FirstName,
                    secondaryTech.LastName,
                    secondaryTech.EmployeeId);

                participantRows.Add(new TicketWriteUpSubmissionTechnicianEntity
                {
                    SubmissionId = submission.Id,
                    TechnicianId = secondaryTech.Id,
                    EmployeeId = TrimForColumn(secondaryTech.EmployeeId, 100) ?? "Unknown",
                    TechnicianName = TrimForColumn(secondaryName, 150) ?? "Unknown",
                    IsSubmitter = primaryTech == null,
                    CreatedAt = changedAt
                });

                if (primaryTech == null)
                {
                    submission.SubmittedByTechnicianId = secondaryTech.Id;
                    submission.SubmittedByEmployeeId = TrimForColumn(secondaryTech.EmployeeId, 100) ?? "Unknown";
                    submission.SubmittedByName = TrimForColumn(secondaryName, 150) ?? "Unknown";
                }
            }

            _db.TicketWriteUpSubmissionTechnicians.AddRange(participantRows);

            return null;
        }

        private static TechnicianEntity? ResolveTechnicianForHistoryEdit(
            string? selectedText,
            IReadOnlyList<TechnicianEntity> technicians)
        {
            var text = NormalizeEditableSiteHistoryText(selectedText);

            if (string.IsNullOrWhiteSpace(text))
                return null;

            return technicians.FirstOrDefault(t =>
                string.Equals(
                    t.EmployeeId?.Trim(),
                    text,
                    StringComparison.OrdinalIgnoreCase) ||

                string.Equals(
                    FormatTechnicianName(t.FirstName, t.LastName, t.EmployeeId),
                    text,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeComparableSiteHistoryText(string? value)
        {
            return NormalizeEditableSiteHistoryText(value) ?? "";
        }

        private static string? NormalizeEditableSiteHistoryText(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (text == "—")
                return null;

            return text;
        }

        // Identifies the unique client-submission constraint used to resolve a rare
        // simultaneous retry as an already completed write-up rather than an error.
        private static bool IsDuplicateClientSubmissionIdException(
            DbUpdateException ex)
        {
            var message =
                ex.InnerException?.Message ??
                ex.Message;

            return message.Contains(
                       "ux_ticket_writeup_submissions_client_submission_id",
                       StringComparison.OrdinalIgnoreCase) ||
                   (
                       message.Contains(
                           "Duplicate",
                           StringComparison.OrdinalIgnoreCase) &&
                       message.Contains(
                           "client_submission_id",
                           StringComparison.OrdinalIgnoreCase)
                   );
        }

        private async Task<EmailSendResult> TrySendPublishedAssignmentTicketModifiedEmailAsync(
            long ticketId,
            string changeTitle,
            IReadOnlyList<string> changeLines,
            CancellationToken ct)
        {
            try
            {
                var assignmentDate = ActiveAssignmentDate;
                var emailDate = DateTime.Today.Date;
                var changedAt = DateTime.Now;

                var publishedTarget = await _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == assignmentDate &&
                        x.TicketId == ticketId)
                    .OrderByDescending(x => x.PublishedAt)
                    .ThenByDescending(x => x.PublishedVersion)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);

                if (publishedTarget == null)
                {
                    return new EmailSendResult
                    {
                        Status = "Skipped",
                        Message = $"Ticket {ticketId} is not on a published Daily Assignment route."
                    };
                }

                var targetType = (publishedTarget.TargetType ?? string.Empty).Trim();

                var targetRowsQuery = _db.DailyTicketAssignmentPublished
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == assignmentDate &&
                        x.TargetType == targetType);

                if (targetType.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                {
                    targetRowsQuery = targetRowsQuery
                        .Where(x => x.TechnicianId == publishedTarget.TechnicianId);
                }
                else
                {
                    targetRowsQuery = targetRowsQuery
                        .Where(x => x.TruckId == publishedTarget.TruckId);
                }

                var latestPublishedVersion = await targetRowsQuery
                    .Select(x => (int?)x.PublishedVersion)
                    .MaxAsync(ct);

                if (!latestPublishedVersion.HasValue)
                {
                    return new EmailSendResult
                    {
                        Status = "Skipped",
                        Message = $"No current published route was found for ticket {ticketId}."
                    };
                }

                var currentRouteRows = await targetRowsQuery
                    .Where(x => x.PublishedVersion == latestPublishedVersion.Value)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToListAsync(ct);

                if (currentRouteRows.Count == 0)
                {
                    return new EmailSendResult
                    {
                        Status = "Skipped",
                        Message = $"Current published route is empty for ticket {ticketId}."
                    };
                }

                var routeTicketIds = currentRouteRows
                    .Select(x => x.TicketId)
                    .Distinct()
                    .ToList();

                var ticketsById = await _db.Tickets
                    .AsNoTracking()
                    .Where(x => routeTicketIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct);

                if (!ticketsById.TryGetValue(ticketId, out var changedTicket))
                {
                    return new EmailSendResult
                    {
                        Status = "Skipped",
                        Message = $"Ticket {ticketId} was not found while building modified Daily Assignment email."
                    };
                }

                var truckId = publishedTarget.TruckId;
                var technicianId = publishedTarget.TechnicianId;

                var truckNumberDisplay = await ResolveTicketTruckNumberDisplayAsync(
                    ticketId,
                    emailDate,
                    ct);

                var recipients = new List<WriteUpEmailRecipientInfo>();
                var targetDisplay = "";

                if (truckId.HasValue)
                {
                    var truckNumber = await _db.Trucks
                        .AsNoTracking()
                        .Where(x => x.Id == truckId.Value)
                        .Select(x => x.TruckNumber)
                        .FirstOrDefaultAsync(ct);

                    truckNumberDisplay = string.IsNullOrWhiteSpace(truckNumber)
                        ? ""
                        : $"Truck {truckNumber.Trim()}";

                    var rosterRows = await (
                        from roster in _db.TruckRosters.AsNoTracking()
                        join tech in _db.Technicians.AsNoTracking()
                            on roster.TechnicianId equals tech.Id
                        where roster.WorkDate == emailDate &&
                              roster.TruckId == truckId.Value &&
                              tech.IsActive
                        select new
                        {
                            tech.EmployeeId,
                            tech.FirstName,
                            tech.LastName,
                            tech.EmailAddress
                        })
                        .ToListAsync(ct);

                    var techNames = rosterRows
                        .Select(x => FormatTechnicianName(
                            x.FirstName,
                            x.LastName,
                            x.EmployeeId))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

                    targetDisplay = FormatCrewDisplayText(techNames);

                    recipients = rosterRows
                        .Select(x => new WriteUpEmailRecipientInfo
                        {
                            Name = FormatTechnicianName(
                                x.FirstName,
                                x.LastName,
                                x.EmployeeId),
                            EmailAddress = (x.EmailAddress ?? string.Empty).Trim()
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.EmailAddress))
                        .GroupBy(x => x.EmailAddress, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .OrderBy(x => x.Name)
                        .ToList();
                }

                if (recipients.Count == 0 && technicianId.HasValue)
                {
                    var technician = await _db.Technicians
                        .AsNoTracking()
                        .Where(x => x.Id == technicianId.Value && x.IsActive)
                        .Select(x => new
                        {
                            x.EmployeeId,
                            x.FirstName,
                            x.LastName,
                            x.EmailAddress
                        })
                        .FirstOrDefaultAsync(ct);

                    if (technician != null)
                    {
                        targetDisplay = FormatTechnicianName(
                            technician.FirstName,
                            technician.LastName,
                            technician.EmployeeId);

                        if (!string.IsNullOrWhiteSpace(technician.EmailAddress))
                        {
                            recipients.Add(new WriteUpEmailRecipientInfo
                            {
                                Name = targetDisplay,
                                EmailAddress = technician.EmailAddress.Trim()
                            });
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(targetDisplay))
                    targetDisplay = "Daily Assignment Target";

                var emailSequence =
                    await _dailyAssignmentEmailSequence.GetNextAsync(
                        targetDisplay,
                        emailDate,
                        ct);

                var subject =
                    $"{targetDisplay} - " +
                    $"{emailSequence.Title} - " +
                    $"{emailDate:MM/dd/yyyy}";

                var body =
                    BuildPublishedAssignmentTicketModifiedEmailBody(
                        emailDate,
                        targetDisplay,
                        truckNumberDisplay,
                        changedAt,
                        emailSequence.Title,
                        changeTitle,
                        changeLines,
                        changedTicket,
                        currentRouteRows,
                        ticketsById);

                var allEmailsAddress = await GetAllEmailsAddressAsync(ct);

                return await _emailService.SendAsync(
                    new EmailSendRequest
                    {
                        EmailType = "DailyAssignment",

                        ToAddresses = recipients
                            .Select(x => x.EmailAddress)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),

                        CcAddresses = string.IsNullOrWhiteSpace(allEmailsAddress)
                            ? Array.Empty<string>()
                            : new[] { allEmailsAddress },

                        Subject = subject,
                        Body = body,
                        IsHtml = true,

                        RelatedTicketId = changedTicket.Id,
                        RelatedSite = changedTicket.Site,

                        CreatedBy = "Ticket Update"
                    },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Daily Assignment update email failed for TicketId={TicketId}",
                    ticketId);

                return new EmailSendResult
                {
                    Status = "Failed",
                    Message = ex.Message
                };
            }
        }
        private static string BuildPublishedAssignmentTicketModifiedEmailBody(
            DateTime workDate,
            string targetDisplay,
            string truckNumberDisplay,
            DateTime changedAt,
            string emailTitle,
            string changeTitle,
            IReadOnlyList<string> changeLines,
            TicketEntity changedTicket,
            IReadOnlyList<DailyTicketAssignmentPublishedEntity> currentRouteRows,
            IReadOnlyDictionary<long, TicketEntity> ticketsById)
        {
            static string H(string? value)
                => WebUtility.HtmlEncode((value ?? string.Empty).Trim());

            static string DashIfBlank(string? value)
            {
                var clean = (value ?? string.Empty).Trim();

                return string.IsNullOrWhiteSpace(clean)
                    ? "—"
                    : WebUtility.HtmlEncode(clean);
            }

            var truckRowHtml = string.IsNullOrWhiteSpace(truckNumberDisplay)
                ? ""
                : $$"""
                      <tr>
                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Truck</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(truckNumberDisplay)}}</td>
                        <td></td>
                        <td></td>
                      </tr>
                  """;

            var sb = new StringBuilder();

            sb.AppendLine($$"""
                <!DOCTYPE html>
                <html>
                <body style="margin:0; padding:0; background:#f3f4f6; font-family:Segoe UI, Arial, sans-serif; color:#111827;">
                  <div style="max-width:1100px; margin:0 auto; padding:24px;">
                    <div style="background:#ffffff; border:1px solid #d1d5db; border-radius:12px; overflow:hidden;">
                      <div style="background:#1f2937; color:#ffffff; padding:18px 22px;">
                        <div style="font-size:22px; font-weight:700;">{{H(emailTitle)}}</div>
                      </div>

                      <div style="padding:18px 22px;">
                        <table cellpadding="0" cellspacing="0" style="width:100%; margin-bottom:18px; border-collapse:collapse;">
                          <tr>
                            <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Date</td>
                            <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{workDate:MM/dd/yyyy}}</td>

                            <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Assigned To</td>
                            <td style="font-size:14px; font-weight:600; padding:3px 0;">{{H(targetDisplay)}}</td>
                          </tr>
                          <tr>
                            <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Changed At</td>
                            <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{changedAt:MM/dd/yyyy HH:mm}}</td>

                            <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Changed Ticket</td>
                            <td style="font-size:14px; font-weight:600; padding:3px 0;">{{DashIfBlank(changedTicket.Site)}}</td>
                          </tr>
                          {{truckRowHtml}}
                        </table>

                        <div style="font-size:15px; font-weight:700; margin:0 0 8px 0;">Ticket Details Changed</div>

                        <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db; margin-bottom:18px;">
                          <thead>
                            <tr style="background:#e5e7eb;">
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db; width:220px;">Change</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Details</th>
                            </tr>
                          </thead>
                          <tbody>
                            <tr>
                              <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:700;">{{H(changeTitle)}}</td>
                              <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">
                """);

            if (changeLines.Count == 0)
            {
                sb.AppendLine("                —");
            }
            else
            {
                foreach (var line in changeLines)
                    sb.AppendLine($"                {H(line)}<br/>");
            }

            sb.AppendLine("""
                              </td>
                            </tr>
                          </tbody>
                        </table>

                        <div style="font-size:15px; font-weight:700; margin:0 0 8px 0;">Current Route</div>

                        <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db;">
                          <thead>
                            <tr style="background:#e5e7eb;">
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">#</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Site</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification Name</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Problem</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Work Order</th>
                              <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">WO Type</th>
                            </tr>
                          </thead>
                          <tbody>
                """);

            var routeOrder = 0;

            foreach (var row in currentRouteRows
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.Id))
            {
                if (!ticketsById.TryGetValue(row.TicketId, out var ticket))
                    continue;

                routeOrder++;

                var background = routeOrder % 2 == 0
                    ? "#f9fafb"
                    : "#ffffff";

                var rowBorder = row.TicketId == changedTicket.Id
                    ? "border:2px solid #f59e0b;"
                    : "border:1px solid #d1d5db;";

                sb.AppendLine($$"""
                    <tr style="background:{{background}};">
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}} font-weight:600;">{{routeOrder}}</td>
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}} font-weight:700;">{{DashIfBlank(ticket.Site)}}</td>
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}}">{{DashIfBlank(ticket.NotificationName)}}</td>
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}}">{{DashIfBlank(ticket.Problem)}}</td>
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}}">{{DashIfBlank(ticket.Notification)}}</td>
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}}">{{DashIfBlank(ticket.CurrentWorkOrder)}}</td>
                        <td style="font-size:13px; padding:9px 10px; {{rowBorder}}">{{DashIfBlank(NormalizeWorkOrderType(ticket.WorkOrderClass))}}</td>
                    </tr>
                    """);
            }

            if (routeOrder == 0)
            {
                sb.AppendLine("""
                    <tr>
                        <td colspan="7" style="font-size:13px; padding:14px 10px; border:1px solid #d1d5db; color:#6b7280; font-style:italic;">
                        No route details were available.
                        </td>
                    </tr>
                    """);
            }

            sb.AppendLine("""
                            </tbody>
                        </table>

                        <div style="font-size:12px; color:#6b7280; margin-top:16px;">
                            This message was generated by SmartGridSuite.
                        </div>
                        </div>
                    </div>
                    </div>
                </body>
                </html>
                """);

            return sb.ToString();
        }

        private async Task<EmailSendResult> TrySendWriteUpSubmittedEmailAsync(
            long ticketId,
            string submittedWriteUp,
            DateTime submittedAt,
            SubmittedWorkInfo submittedWork,
            CancellationToken ct)
        {
            try
            {
                var ticket = await _db.Tickets
                    .AsNoTracking()
                    .Include(x => x.TaskCategory)
                    .FirstOrDefaultAsync(
                        x => x.Id == ticketId,
                        ct);

                if (ticket == null)
                {
                    return new EmailSendResult
                    {
                        Status = "Skipped",
                        Message =
                            $"Ticket {ticketId} was not found after write-up submission."
                    };
                }

                var submittedByName =
                    string.IsNullOrWhiteSpace(submittedWork.SubmittedByName)
                        ? "Unknown"
                        : submittedWork.SubmittedByName.Trim();

                var submitterParticipant =
                    submittedWork.Participants
                        .FirstOrDefault(x => x.IsSubmitter);

                var submitterEmail =
                    (submitterParticipant?.EmailAddress ?? string.Empty)
                    .Trim();

                var allEmailsAddress =
                    await GetAllEmailsAddressAsync(ct);

                var recipients =
                    string.IsNullOrWhiteSpace(allEmailsAddress)
                        ? await LoadWriteUpEmailRecipientsAsync(ct)
                        : new List<WriteUpEmailRecipientInfo>
                        {
                    new()
                    {
                        Name = "SmartGridSuite Write-Ups",
                        EmailAddress = allEmailsAddress
                    }
                        };

                /*
                 * In normal production delivery, send the write-up to every
                 * technician snapshotted as a participant, in addition to the
                 * configured Dispatch/Admin recipients.
                 *
                 * When AllEmailsAddress is configured, it remains an intentional
                 * testing override and all delivery is redirected there.
                 */
                if (string.IsNullOrWhiteSpace(allEmailsAddress))
                {
                    recipients.AddRange(
                        submittedWork.Participants
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(
                                    x.EmailAddress))
                            .Select(x =>
                                new WriteUpEmailRecipientInfo
                                {
                                    Name =
                                        x.TechnicianName,

                                    EmailAddress =
                                        x.EmailAddress.Trim()
                                }));
                }

                var truckNumberDisplay =
                    await ResolveTicketTruckNumberDisplayAsync(
                        ticket.Id,
                        submittedAt.Date,
                        ct);

                var subject =
                    $"{ticket.Site} - {submittedByName} - Write-Up Submitted";

                var body =
                    BuildWriteUpSubmittedEmailBody(
                        ticket,
                        submittedByName,
                        submittedAt,
                        truckNumberDisplay,
                        submittedWriteUp);

                return await _emailService.SendAsync(
                    new EmailSendRequest
                    {
                        EmailType = "WriteUp",

                        ToAddresses = recipients
                            .Select(x => x.EmailAddress)
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList(),

                        ReplyToAddresses =
                            string.IsNullOrWhiteSpace(submitterEmail)
                                ? Array.Empty<string>()
                                : new[] { submitterEmail },

                        FromAddress = submitterEmail,
                        FromDisplayName = submittedByName,

                        Subject = subject,
                        Body = body,
                        IsHtml = true,

                        RelatedTicketId = ticket.Id,
                        RelatedSite = ticket.Site,

                        CreatedBy = submittedByName
                    },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Write-up submission email failed. TicketId={TicketId}",
                    ticketId);

                return new EmailSendResult
                {
                    Status = "Failed",
                    Message = ex.Message
                };
            }
        }

        private async Task<List<WriteUpEmailRecipientInfo>> LoadWriteUpEmailRecipientsAsync(CancellationToken ct)
        {
            /*
             * Send submitted write-up notifications to active Dispatch/Admin users
             * who have an email address configured in Administration > Technicians.
             */
            var recipients = await _db.Technicians
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    !string.IsNullOrWhiteSpace(x.EmailAddress) &&
                    x.TechnicianRoles.Any(role =>
                        role.Role.Code == "DISPATCH" ||
                        role.Role.Code == "ADMIN"))
                .Select(x => new WriteUpEmailRecipientInfo
                {
                    Name = ((x.FirstName ?? "") + " " + (x.LastName ?? "")).Trim(),
                    EmailAddress = x.EmailAddress ?? ""
                })
                .ToListAsync(ct);

            return recipients
                .Where(x => !string.IsNullOrWhiteSpace(x.EmailAddress))
                .GroupBy(x => x.EmailAddress, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Name)
                .ToList();
        }

        private static string BuildWriteUpSubmittedEmailBody(
            TicketEntity ticket, 
            string submittedByName, 
            DateTime submittedAt, 
            string truckNumberDisplay, 
            string submittedWriteUp)
        {
            static string H(string? value) => WebUtility.HtmlEncode((value ?? string.Empty).Trim());

            static string DashIfBlank(string? value)
            {
                var clean = (value ?? string.Empty).Trim();

                return string.IsNullOrWhiteSpace(clean)
                    ? "—"
                    : WebUtility.HtmlEncode(clean);
            }

            static string WriteUpTextHtml(string? value)
            {
                var raw = value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(raw))
                    return "—";

                return WebUtility.HtmlEncode(raw);
            }

            var truckRowHtml = string.IsNullOrWhiteSpace(truckNumberDisplay)
                ? ""
                : $$"""
                      <tr>
                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Truck</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(truckNumberDisplay)}}</td>
                        <td></td>
                        <td></td>
                      </tr>
                  """;

            var writeUpHtml = WriteUpTextHtml(submittedWriteUp);

            var workOrderType = NormalizeWorkOrderType(ticket.WorkOrderClass);

            var sb = new StringBuilder();

            sb.AppendLine("""
                <!DOCTYPE html>
                <html>
                <body style="margin:0; padding:0; background:#f3f4f6; font-family:Segoe UI, Arial, sans-serif; color:#111827;">
                  <div style="max-width:1000px; margin:0 auto; padding:24px;">
                    <div style="background:#ffffff; border:1px solid #d1d5db; border-radius:12px; overflow:hidden;">
                      <div style="background:#1f2937; color:#ffffff; padding:18px 22px;">
                        <div style="font-size:22px; font-weight:700;">Write-Up Submitted</div>
                      </div>
                """);

            sb.AppendLine($$"""
                  <div style="padding:18px 22px;">
                    <table cellpadding="0" cellspacing="0" style="width:100%; margin-bottom:18px; border-collapse:collapse;">
                      <tr>
                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Submitted By</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{H(submittedByName)}}</td>

                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Submitted At</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 0;">{{submittedAt:MM/dd/yyyy HH:mm}}</td>
                      </tr>
                      <tr>
                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Site</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 24px 3px 0;">{{DashIfBlank(ticket.Site)}}</td>

                        <td style="font-size:13px; color:#6b7280; padding:3px 14px 3px 0;">Status</td>
                        <td style="font-size:14px; font-weight:600; padding:3px 0;">{{DashIfBlank(ticket.Status)}}</td>
                      </tr>
                      {{truckRowHtml}}
                    </table>

                    <table cellpadding="0" cellspacing="0" style="width:100%; border-collapse:collapse; border:1px solid #d1d5db; margin-bottom:18px;">
                      <thead>
                        <tr style="background:#e5e7eb;">
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Site</th>
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification Name</th>
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Problem</th>
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Notification</th>
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Work Order</th>
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">WO Type</th>
                          <th style="text-align:left; font-size:12px; padding:9px 10px; border:1px solid #d1d5db;">Assigned To</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db; font-weight:700;">{{DashIfBlank(ticket.Site)}}</td>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.NotificationName)}}</td>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.Problem)}}</td>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.Notification)}}</td>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.CurrentWorkOrder)}}</td>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(workOrderType)}}</td>
                          <td style="font-size:13px; padding:9px 10px; border:1px solid #d1d5db;">{{DashIfBlank(ticket.AssignedTech)}}</td>
                        </tr>
                      </tbody>
                    </table>

                        <div style="font-size:15px; font-weight:700; margin-bottom:8px;">Submitted Write-Up</div>

                <div style="background:#f9fafb; border:1px solid #d1d5db; border-radius:10px; padding:12px 14px;">
                  <pre style="margin:0; font-family:Segoe UI, Arial, sans-serif; font-size:14px; line-height:19px; color:#111827; white-space:pre-wrap; word-wrap:break-word;">{{writeUpHtml}}</pre>
                </div>

                        <div style="font-size:12px; color:#6b7280; margin-top:16px;">
                          This message was generated by SmartGridSuite.
                        </div>
                      </div>
                    </div>
                  </div>
                </body>
                </html>
                """);

            return sb.ToString();
        }

        private async Task<string> ResolveTicketTruckNumberDisplayAsync(
            long ticketId,
            DateTime workDate,
            CancellationToken ct)
        {
            var publishedTarget = await _db.DailyTicketAssignmentPublished
                .AsNoTracking()
                .Where(x =>
                    x.AssignmentDate == ActiveAssignmentDate &&
                    x.TicketId == ticketId)
                .OrderByDescending(x => x.PublishedAt)
                .ThenByDescending(x => x.PublishedVersion)
                .ThenByDescending(x => x.Id)
                .Select(x => new
                {
                    x.TruckId,
                    x.TechnicianId
                })
                .FirstOrDefaultAsync(ct);

            uint? truckId = publishedTarget?.TruckId;

            /*
             * Older published rows, individual-looking technician routes, or migrated rows
             * may not have TruckId stored. In that case, use the published owner technician
             * and today's/submission day's truck roster to recover the truck context.
             */
            if (!truckId.HasValue &&
                publishedTarget?.TechnicianId is uint publishedTechnicianId)
            {
                truckId = await _db.TruckRosters
                    .AsNoTracking()
                    .Where(x =>
                        x.WorkDate == workDate.Date &&
                        x.TechnicianId == publishedTechnicianId)
                    .Select(x => (uint?)x.TruckId)
                    .FirstOrDefaultAsync(ct);
            }

            if (!truckId.HasValue)
            {
                var activeTarget = await _db.DailyTicketAssignments
                    .AsNoTracking()
                    .Where(x =>
                        x.AssignmentDate == ActiveAssignmentDate &&
                        x.TicketId == ticketId)
                    .OrderByDescending(x => x.UpdatedAt)
                    .ThenByDescending(x => x.Id)
                    .Select(x => new
                    {
                        x.TruckId,
                        x.TechnicianId
                    })
                    .FirstOrDefaultAsync(ct);

                truckId = activeTarget?.TruckId;

                if (!truckId.HasValue &&
                    activeTarget?.TechnicianId is uint activeTechnicianId)
                {
                    truckId = await _db.TruckRosters
                        .AsNoTracking()
                        .Where(x =>
                            x.WorkDate == workDate.Date &&
                            x.TechnicianId == activeTechnicianId)
                        .Select(x => (uint?)x.TruckId)
                        .FirstOrDefaultAsync(ct);
                }
            }

            if (!truckId.HasValue)
                return "";

            var truckNumber = await _db.Trucks
                .AsNoTracking()
                .Where(x => x.Id == truckId.Value)
                .Select(x => x.TruckNumber)
                .FirstOrDefaultAsync(ct);

            return string.IsNullOrWhiteSpace(truckNumber)
                ? ""
                : $"Truck {truckNumber.Trim()}";
        }

        private async Task<string> GetAllEmailsAddressAsync(CancellationToken ct)
        {
            var value = await _db.AppSettings
                .AsNoTracking()
                .Where(x => x.SettingKey == "Email.AllEmailsAddress")
                .Select(x => x.SettingValue)
                .FirstOrDefaultAsync(ct);

            return (value ?? string.Empty).Trim();
        }

        private sealed class WriteUpEmailRecipientInfo
        {
            public string Name { get; set; } = "";

            public string EmailAddress { get; set; } = "";
        }
    }
}