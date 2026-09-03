using Microsoft.AspNetCore.Mvc;
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