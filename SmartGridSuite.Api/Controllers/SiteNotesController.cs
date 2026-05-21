using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.SiteNotes;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/site-notes")]
    public class SiteNotesController : ControllerBase
    {
        private readonly SmartGridDbContext _db;

        public SiteNotesController(SmartGridDbContext db)
        {
            _db = db;
        }

        [HttpGet("{siteId}")]
        public async Task<ActionResult<List<SiteNoteDto>>> GetBySite(string siteId, CancellationToken ct)
        {
            var cleanSiteId = NormalizeSiteId(siteId);

            if (string.IsNullOrWhiteSpace(cleanSiteId))
                return Ok(new List<SiteNoteDto>());

            var notes = await _db.SiteNotes
                .AsNoTracking()
                .Where(x => x.IsActive && x.SiteId == cleanSiteId)
                .OrderBy(x => x.NoteType)
                .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .Select(x => MapToDto(x))
                .ToListAsync(ct);

            return Ok(notes);
        }

        [HttpPost]
        public async Task<ActionResult<SiteNoteDto>> Create([FromBody] CreateSiteNoteRequest request, CancellationToken ct)
        {
            var siteId = NormalizeSiteId(request.SiteId);
            var noteText = (request.NoteText ?? string.Empty).Trim();
            var noteType = NormalizeNoteType(request.NoteType);
            var createdBy = NormalizeUser(request.CreatedBy);

            if (string.IsNullOrWhiteSpace(siteId))
                return BadRequest("Site ID is required.");

            if (string.IsNullOrWhiteSpace(noteText))
                return BadRequest("Note text is required.");

            var now = DateTime.Now;

            var entity = new SiteNoteEntity
            {
                SiteId = siteId,
                NoteType = noteType,
                NoteText = noteText,
                IsActive = true,
                CreatedBy = createdBy,
                CreatedAt = now,
                UpdatedBy = null,
                UpdatedAt = null,
                DeletedBy = null,
                DeletedAt = null
            };

            _db.SiteNotes.Add(entity);
            await _db.SaveChangesAsync(ct);

            return Ok(MapToDto(entity));
        }

        [HttpPut("{id:long}")]
        public async Task<ActionResult<SiteNoteDto>> Update(ulong id, [FromBody] UpdateSiteNoteRequest request, CancellationToken ct)
        {
            if (id != request.Id)
                return BadRequest("Route id does not match request id.");

            var entity = await _db.SiteNotes.FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null || !entity.IsActive)
                return NotFound();

            var noteText = (request.NoteText ?? string.Empty).Trim();
            var noteType = NormalizeNoteType(request.NoteType);
            var updatedBy = NormalizeUser(request.UpdatedBy);

            if (string.IsNullOrWhiteSpace(noteText))
                return BadRequest("Note text is required.");

            entity.NoteType = noteType;
            entity.NoteText = noteText;
            entity.UpdatedBy = updatedBy;
            entity.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(MapToDto(entity));
        }

        [HttpPost("{id:long}/delete")]
        public async Task<IActionResult> Delete(ulong id, [FromBody] DeleteSiteNoteRequest request, CancellationToken ct)
        {
            var entity = await _db.SiteNotes.FirstOrDefaultAsync(x => x.Id == id, ct);

            if (entity == null || !entity.IsActive)
                return NotFound();

            entity.IsActive = false;
            entity.DeletedBy = NormalizeUser(request.DeletedBy);
            entity.DeletedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        private static SiteNoteDto MapToDto(SiteNoteEntity entity)
        {
            return new SiteNoteDto
            {
                Id = entity.Id,
                SiteId = entity.SiteId,
                NoteType = entity.NoteType ?? "",
                NoteText = entity.NoteText,
                IsActive = entity.IsActive,
                CreatedBy = entity.CreatedBy,
                CreatedAt = entity.CreatedAt,
                UpdatedBy = entity.UpdatedBy ?? "",
                UpdatedAt = entity.UpdatedAt
            };
        }

        private static string NormalizeSiteId(string? siteId)
        {
            return (siteId ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string? NormalizeNoteType(string? noteType)
        {
            var clean = (noteType ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(clean))
                return "General";

            return clean;
        }

        private static string NormalizeUser(string? user)
        {
            var clean = (user ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(clean)
                ? "Unknown"
                : clean;
        }
    }
}