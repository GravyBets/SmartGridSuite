using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Settings;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/app-settings")]
    public sealed class AppSettingsController : ControllerBase
    {
        private const string IgsdPortalUrlKey = "IGSD_PORTAL_URL";
        private const string RangeExtenderLinkUrlKey = "RANGE_EXTENDER_LINK_URL";
        private readonly SmartGridDbContext _db;

        public AppSettingsController(SmartGridDbContext db)
        {
            _db = db;
        }

        [HttpGet("igsd-portal-url")]
        public async Task<ActionResult<IgsdPortalUrlDto>> GetIgsdPortalUrl(CancellationToken ct)
        {
            var row = await _db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SettingKey == IgsdPortalUrlKey, ct);

            return Ok(new IgsdPortalUrlDto
            {
                Url = row?.SettingValue?.Trim() ?? string.Empty
            });
        }

        [HttpPut("igsd-portal-url")]
        public async Task<ActionResult<IgsdPortalUrlDto>> UpdateIgsdPortalUrl(
            [FromBody] UpdateIgsdPortalUrlRequest req,
            CancellationToken ct)
        {
            var url = (req.Url ?? string.Empty).Trim();

            var row = await _db.AppSettings
                .FirstOrDefaultAsync(x => x.SettingKey == IgsdPortalUrlKey, ct);

            if (row is null)
            {
                row = new AppSettingEntity
                {
                    SettingKey = IgsdPortalUrlKey,
                    SettingValue = url,
                    UpdatedAt = DateTime.Now
                };

                _db.AppSettings.Add(row);
            }
            else
            {
                row.SettingValue = url;
                row.UpdatedAt = DateTime.Now;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new IgsdPortalUrlDto
            {
                Url = url
            });
        }

        [HttpGet("range-extender-link-url")]
        public async Task<ActionResult<RangeExtenderLinkUrlDto>> GetRangeExtenderLinkUrl(CancellationToken ct)
        {
            var row = await _db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SettingKey == RangeExtenderLinkUrlKey, ct);

            return Ok(new RangeExtenderLinkUrlDto
            {
                Url = row?.SettingValue?.Trim() ?? string.Empty
            });
        }

        [HttpPut("range-extender-link-url")]
        public async Task<ActionResult<RangeExtenderLinkUrlDto>> UpdateRangeExtenderLinkUrl(
            [FromBody] UpdateRangeExtenderLinkUrlRequest req,
            CancellationToken ct)
        {
            var url = (req.Url ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(url) &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                return BadRequest("Enter a valid http or https URL, or leave it blank.");
            }

            var row = await _db.AppSettings
                .FirstOrDefaultAsync(x => x.SettingKey == RangeExtenderLinkUrlKey, ct);

            if (row is null)
            {
                row = new AppSettingEntity
                {
                    SettingKey = RangeExtenderLinkUrlKey,
                    SettingValue = url,
                    UpdatedAt = DateTime.Now
                };

                _db.AppSettings.Add(row);
            }
            else
            {
                row.SettingValue = url;
                row.UpdatedAt = DateTime.Now;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new RangeExtenderLinkUrlDto
            {
                Url = url
            });
        }
    }
}