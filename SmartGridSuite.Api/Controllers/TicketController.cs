using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Tickets;
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
            var q = _db.Tickets.AsNoTracking();

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
                .Take(500)
                .ToListAsync();

            var result = rows.Select(t => new TicketListItemDto(
                t.Id,
                t.Site,
                t.NotificationName ?? "",
                t.Notification ?? "",
                t.Status,
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

        [HttpPost]
        public async Task<ActionResult<CreateTicketResponse>> Create([FromBody] CreateTicketRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Site)) return BadRequest("Site required");
            if (string.IsNullOrWhiteSpace(req.Problem)) return BadRequest("Problem required");

            // Notification: treat blank as NULL
            string? notif = string.IsNullOrWhiteSpace(req.Notification) ? null : req.Notification.Trim();

            // if provided, must be 10 digits
            if (notif is not null && !Regex.IsMatch(notif, @"^\d{10}$"))
                return BadRequest("Notification must be 10 digits when provided");

            // Friendly duplicate check (lets UI show message without parsing DB errors)
            if (notif is not null)
            {
                var exists = await _db.Tickets.AsNoTracking().AnyAsync(t => t.Notification == notif);
                if (exists)
                    return Conflict($"A ticket already exists with Notification {notif}.");
            }

            // Work order optional, 9 digits if provided
            var wo = (req.WorkOrder ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(wo) && !Regex.IsMatch(wo, @"^\d{9}$"))
                return BadRequest("WorkOrder must be 9 digits when provided");

            var assignedTech = string.IsNullOrWhiteSpace(req.AssignedTech) ? "(Unassigned)" : req.AssignedTech.Trim();
            var status = assignedTech == "(Unassigned)" ? "Open" : "Assigned";

            var createdBy = string.IsNullOrWhiteSpace(req.CreatedBy) ? "Unknown" : req.CreatedBy.Trim();

            var now = DateTime.Now;

            var entity = new TicketEntity
            {
                Site = req.Site.Trim(),
                NotificationName = (req.NotificationName ?? "").Trim(),
                Notification = notif, // <-- NULL when blank

                Status = status,
                AssignedTech = assignedTech,

                CreatedAt = now,
                LastActivityAt = now,

                CurrentWorkOrder = string.IsNullOrWhiteSpace(wo) ? null : wo,
                WorkOrderClass = string.IsNullOrWhiteSpace(wo) ? null : (req.WorkOrderClass ?? "Maint").Trim(),
                GroupCode = (req.GroupCode ?? "").Trim(),
                PriorityDays = (byte)Math.Clamp(req.PriorityDays, 0, 255),

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
                // Safety net in case two dispatchers submit the same notification simultaneously
                var msg = ex.InnerException?.Message ?? ex.Message;
                if (msg.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
                    return Conflict($"A ticket already exists with Notification {notif}.");

                throw;
            }

            return Ok(new CreateTicketResponse(entity.Id));
        }
    }
}