using Microsoft.AspNetCore.Mvc;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.SiteDashboard;
using SmartGridSuite.Api.Services.SystemHealth;
using SmartGridSuite.Contracts.Administration;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/admin/system-health")]
    public sealed class AdminSystemHealthController : ControllerBase
    {
        private readonly SystemHealthService _systemHealthService;

        public AdminSystemHealthController(SystemHealthService systemHealthService)
        {
            _systemHealthService = systemHealthService;
        }

        [HttpPost("test-parent-db")]
        public async Task<ActionResult<ParentDatabaseTestResponse>> TestParentDatabase(
            [FromServices] ParentDatabaseConnectionFactory factory, CancellationToken ct)
        {
            return Ok(await _systemHealthService.TestParentDatabaseAsync(factory, ct));
        }

        [HttpPost("refresh-parent-cache")]
        public async Task<ActionResult<ParentCacheRefreshResponse>> RefreshParentCache(
            [FromServices]
            SiteDashboardCacheRefreshService refreshService,
            CancellationToken ct)
        {
            var result = await refreshService.RefreshAsync(ct);

            var health = await _systemHealthService.GetAsync(ct);

            return Ok(new ParentCacheRefreshResponse
            {
                Message =
                        "Parent DB cache snapshot completed successfully. " +
                        $"{result.TotalSiteCount:N0} sites, " +
                        $"{result.TowerCount:N0} towers, " +
                        $"{result.TowerSectorCount:N0} sectors.",

                SyncRunId = result.SyncRunId.ToString(),
                SiteCount = result.TotalSiteCount,
                TowerCount = result.TowerCount,
                SectorCount = result.TowerSectorCount,
                Health = health
            });
        }

        [HttpGet]
        public async Task<ActionResult<SystemHealthDto>> Get(CancellationToken cancellationToken)
        {
            var health =
                await _systemHealthService.GetAsync(
                    cancellationToken);

            return Ok(health);
        }
    }
}