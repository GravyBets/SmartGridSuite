using Microsoft.AspNetCore.Mvc;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.ParentSync.Models;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/site-dashboard")]    
    public sealed class SiteDashboardController : ControllerBase
    {
        private readonly ParentSyncService _parentSyncService;

        public SiteDashboardController(ParentSyncService parentSyncService)
        {
            _parentSyncService = parentSyncService;
        }

        [HttpGet("site-count")]
        public async Task<ActionResult<object>> GetSiteCount(CancellationToken cancellationToken)
        {
            var count = await _parentSyncService.GetSiteCountAsync(cancellationToken);
            return Ok(new { siteCount = count });
        }

        //Which model to use (AMS, DACs, IG...)
        [HttpGet("site-dashboard-route/{siteId}")]
        public async Task<ActionResult<SiteDashboardRouteInfo>> GetSiteDashboardRouteInfo(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetSiteDashboardRouteInfoAsync(siteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{siteId}' was not found in sgc_main.Site." });
            }

            return Ok(row);
        }

        [HttpGet("{siteId}")]
        public async Task<ActionResult<SiteDashboardResponse>> GetSiteDashboard(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return BadRequest("Site ID is required.");
            }

            var dashboard = await _parentSyncService.GetSiteDashboardAsync(siteId, cancellationToken);

            if (dashboard is null)
            {
                return NotFound(new { message = $"Site '{siteId}' was not found or is not yet supported." });
            }

            return Ok(dashboard);
        }

        //AMS
        
        [HttpGet("ams-mr-site/{siteId}")]
        public async Task<ActionResult<AmsMrSiteDashboardRow>> GetAmsMrSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetAmsMrSiteAsync(siteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{siteId}' was not found in sgc_comm.AMS." });
            }

            return Ok(row);
        }


        //DACs
        [HttpGet("dacs-site/{siteId}")]
        public async Task<ActionResult<DacsSiteDashboardRow>> GetDacsSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetDacsSiteAsync(siteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{siteId}' was not found in sgc_equip.Radio700." });
            }

            return Ok(row);
        }

        //IG
        [HttpGet("igsd-site/{siteId}")]
        public async Task<ActionResult<IgsdSiteDashboardRow>> GetIgsdSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetIgsdSiteAsync(siteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{siteId}' was not found in sgc_comm.IGSD." });
            }

            return Ok(row);
        }

        //Range Extenders
        [HttpGet("rx-site/{siteId}")]
        public async Task<ActionResult<RxSiteDashboardRow>> GetRxSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetRxSiteAsync(siteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{siteId}' was not found in sgc_comm.RE." });
            }

            return Ok(row);
        }

        //Towers
        [HttpGet("tower/{topNameId:int}")]
        public async Task<ActionResult<TowerDashboardRow>> GetTower(
            int topNameId, CancellationToken cancellationToken = default)
        {
            if (topNameId <= 0)
            {
                return BadRequest("TopNameId must be greater than zero.");
            }

            var row = await _parentSyncService.GetTowerAsync(topNameId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Tower '{topNameId}' was not found in sgc_tnp.TopName." });
            }

            return Ok(row);
        }

        [HttpGet("tower-search")]
        public async Task<ActionResult<List<TowerSearchRow>>> SearchTowers(
            [FromQuery] string term, [FromQuery] int take = 25, CancellationToken cancellationToken = default)
        {
            term = (term ?? "").Trim();

            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("Search term is required.");
            }

            var rows = await _parentSyncService.SearchTowersAsync(term, take, cancellationToken);
            return Ok(rows);
        }
    }
}