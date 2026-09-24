using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services.SiteDashboard;
using SmartGridSuite.Contracts.Tickets;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SmartGridSuite.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:long}/top-change")]
public sealed class TopChangesController : ControllerBase
{
    private readonly SmartGridDbContext _db;
    private readonly SiteDashboardLookupService _lookup;
    public TopChangesController(SmartGridDbContext db, SiteDashboardLookupService lookup)
        => (_db, _lookup) = (db, lookup);

    [HttpGet]
    public async Task<ActionResult<TopChangeContextDto>> Get(long ticketId, CancellationToken ct)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket == null) return NotFound("Ticket not found.");
        var context = await LoadContextAsync(ticket, ct);
        if (context == null) return BadRequest("TOP changes support MR, DAC and IG sites with available site data.");
        return Ok(context);
    }

    [HttpGet("notice")]
    public async Task<ActionResult<TopChangeDto?>> Notice(long ticketId, CancellationToken ct) =>
        Ok(await _db.TopChanges.AsNoTracking().Where(x => x.TicketId == ticketId && x.ActiveTicketId != null)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct));

    [HttpPost]
    public async Task<ActionResult<TopChangeDto>> Create(long ticketId, CreateTopChangeRequest request, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.RequestedBy))
            return BadRequest("Submission ID and requesting technician are required.");
        var currentTop = (request.CurrentTop ?? request.ExpectedTop ?? "").Trim();
        var currentSector = (request.CurrentSector ?? request.ExpectedSector ?? "").Trim();
        var currentIp = (request.CurrentIp ?? request.ExpectedIp ?? "").Trim();
        if (currentTop.Length == 0 || currentSector.Length == 0 ||
            currentTop.Length > 255 || currentSector.Length > 255 || currentIp.Length > 255)
            return BadRequest("Current TOP and sector are required; current values must be at most 255 characters.");
        if (currentIp.Length > 0 && (currentIp.Split('.').Length != 4 ||
            !IPAddress.TryParse(currentIp, out var currentAddress) || currentAddress.AddressFamily != AddressFamily.InterNetwork))
            return BadRequest("Current IP must be a valid IPv4 address, or blank if unknown.");
        var previous = await _db.TopChanges.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientRequestId == request.ClientRequestId, ct);
        if (previous != null)
            return previous.TicketId == ticketId && previous.NewSectorId == request.NewSectorId
                && previous.OldTop == currentTop && previous.OldSector == currentSector && previous.OldIp == currentIp
                && previous.AlreadyChanged == request.AlreadyChanged && previous.RequestedBy == request.RequestedBy.Trim()
                ? Ok(previous) : Conflict("This submission ID was already used with different details. Reopen the request to review what was saved.");
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.LockTicketAsync(ticketId, ct);
        var ticket = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket == null) return NotFound("Ticket not found.");
        if (await _db.TicketStatuses.AnyAsync(x => x.Name == ticket.Status && x.IsClosed, ct))
            return BadRequest("Reopen the ticket before requesting a TOP change.");
        if (!await _db.TicketStatuses.AnyAsync(x => x.Name == "TOP Change" && x.IsActive && x.SendToDispatchTasks, ct))
            return BadRequest("Install the TOP Change database setup before using this workflow.");
        var actor = await _db.Technicians.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == request.RequestedBy.Trim() && x.IsActive, ct);
        if (actor == null) return BadRequest("Requesting technician could not be resolved.");
        var context = await LoadContextAsync(ticket, ct);
        if (context == null) return BadRequest("Current site data is unavailable or this site type is unsupported.");
        if (context.Request is { State: not "Completed" })
            return Conflict("This ticket already has an active TOP change. Reopen it to review the request.");
        if (context.CurrentTop != request.ExpectedTop || context.CurrentSector != request.ExpectedSector || context.CurrentIp != request.ExpectedIp)
            return Conflict("Site data changed. Reopen the request and review the current values.");
        var sector = context.Sectors.FirstOrDefault(x => x.SectorId == request.NewSectorId);
        if (sector == null) return BadRequest("Choose an active sector from the selected TOP.");
        if (sector.Top == currentTop && sector.Sector == currentSector)
            return BadRequest("Select a different TOP or sector.");
        var row = new TopChangeEntity
        {
            TicketId = ticketId, ActiveTicketId = ticketId, ClientRequestId = request.ClientRequestId,
            Site = ticket.Site, SiteKind = context.SiteKind,
            OldTop = currentTop, OldSector = currentSector, OldIp = currentIp,
            NewTopId = sector.TopId, NewSectorId = sector.SectorId, NewTop = sector.Top, NewSector = sector.Sector,
            AlreadyChanged = request.AlreadyChanged, RequestedBy = actor.EmployeeId,
            RequestedAt = DateTime.Now, State = "PendingIp"
        };
        _db.TopChanges.Add(row);
        ticket.Status = "TOP Change";
        ticket.ActionRequiredOverride = "TOP Change — awaiting IP";
        ticket.LastActivityAt = row.RequestedAt;
        try { await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException)
        {
            // Unique ActiveTicketId prevents simultaneous requests for the same ticket.
            return Conflict("A TOP change may already exist. Refresh the request before retrying.");
        }
        return Ok(row);
    }

    [HttpPost("{id:long}/assign-ip")]
    public async Task<ActionResult<TopChangeDto>> AssignIp(long ticketId, long id, AssignTopChangeIpRequest request, CancellationToken ct)
    {
        if (!IPAddress.TryParse(request.Ip?.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return BadRequest("Enter a valid IPv4 address.");
        if (string.IsNullOrWhiteSpace(request.AssignedBy) || request.AssignedBy.Length > 100)
            return BadRequest("Dispatcher identity is required.");
        // Lock the request so a simultaneous write-up cannot miss the pending/ready transition.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.LockTicketAsync(ticketId, ct);
        var rows = await _db.TopChanges.FromSqlInterpolated(
            $"SELECT * FROM ticket_top_changes WHERE Id = {id} AND TicketId = {ticketId} FOR UPDATE")
            .ToListAsync(ct);
        var row = rows.SingleOrDefault();
        if (row == null) return NotFound();
        if (row.State == "Completed") return Conflict("This TOP change has already been completed by a write-up.");
        if (row.State == "IpReady" && row.NewIp != ip.ToString())
            return Conflict("An IP is already recorded. Verify the existing assignment before changing it.");
        row.NewIp = ip.ToString();
        row.State = "IpReady";
        row.IpAssignedAt ??= DateTime.Now;
        row.IpAssignedBy = request.AssignedBy.Trim();
        var ticket = await _db.Tickets.SingleAsync(x => x.Id == ticketId, ct);
        ticket.ActionRequiredOverride = $"TOP Change — IP ready: {row.NewIp}";
        ticket.LastActivityAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(row);
    }

    private async Task<TopChangeContextDto?> LoadContextAsync(TicketEntity ticket, CancellationToken ct)
    {
        var existing = await _db.TopChanges.AsNoTracking().Where(x => x.TicketId == ticket.Id)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        // Existing request details remain usable even if Parent DB/cache is unavailable.
        if (existing != null && existing.State != "Completed")
            return new TopChangeContextDto { TicketId = ticket.Id, Site = ticket.Site, SiteKind = existing.SiteKind,
                CurrentTop = existing.OldTop, CurrentSector = existing.OldSector, CurrentIp = existing.OldIp,
                DataWarning = "Showing the current values recorded with this request.", Request = existing };
        var lookup = await _lookup.GetAsync(ticket.Site, ct);
        var data = lookup.Dashboard;
        if (data == null || (data.DashboardKind != "AMS" && data.DashboardKind != "DACs" && data.DashboardKind != "IGSD")) return null;
        var json = JsonSerializer.SerializeToElement(data.Data);
        string Value(string key) => json.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var sectors = await (from sector in _db.CacheTowerSectors.AsNoTracking()
            join top in _db.CacheTowers.AsNoTracking() on sector.TopNameId equals top.TopNameId
            where sector.IsActive && top.IsActive && sector.Sector != null && top.TopName != null
            orderby top.TopName, sector.Sector
            select new TopChangeSectorOption { TopId = top.TopNameId, SectorId = sector.TopSiteId,
                Top = top.TopName!, Sector = sector.Sector! }).ToListAsync(ct);
        return new TopChangeContextDto { TicketId = ticket.Id, Site = ticket.Site, SiteKind = data.DashboardKind,
            CurrentTop = Value("TopName"), CurrentSector = Value("TopSector"), CurrentIp = Value("PrimaryCommsIp"),
            DataWarning = data.IsCached
                ? "Current site values: cached fallback because the live Parent DB lookup failed or timed out. Verify before submitting. Dropdown choices: tower cache."
                : "Current site values: live Parent DB. Dropdown choices: tower cache.",
            Sectors = sectors, Request = existing };
    }
}
