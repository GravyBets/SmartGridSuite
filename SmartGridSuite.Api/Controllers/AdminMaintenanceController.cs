using Microsoft.AspNetCore.Mvc;
using SmartGridSuite.Api.Services.SystemHealth;
using SmartGridSuite.Contracts.Administration;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public sealed class AdminMaintenanceController : ControllerBase
    {
        private readonly ApiRestartService _restart;
        public AdminMaintenanceController(ApiRestartService restart) => _restart = restart;

        [HttpPost("restart-api")]
        [RequestSizeLimit(2048)]
        public async Task<ActionResult<RestartApiResponse>> Restart([FromBody] RestartApiRequest request)
        {
            var result = await _restart.RequestAsync(request.Password);
            request.Password = "";
            return StatusCode(result.Status, result.Response);
        }
    }
}
