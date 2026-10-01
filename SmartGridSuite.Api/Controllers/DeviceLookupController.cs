using Microsoft.AspNetCore.Mvc;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Dispatcher;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/device-lookup")]
    public sealed class DeviceLookupController : ControllerBase
    {
        private readonly DeviceLookupService _deviceLookupService;

        public DeviceLookupController(
            DeviceLookupService deviceLookupService)
        {
            _deviceLookupService = deviceLookupService;
        }

        [HttpGet]
        public async Task<ActionResult<DeviceLookupResponseDto>> Search(
            [FromQuery] string query,
            CancellationToken cancellationToken)
        {
            query = (query ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(
                    "Enter a site, IP, SIM, serial number, notification, Work Order, or other identifier.");
            }

            if (query.Length > 250)
            {
                return BadRequest(
                    "Device Lookup searches are limited to 250 characters.");
            }

            var result =
                await _deviceLookupService.SearchAsync(
                    query,
                    cancellationToken);

            return Ok(result);
        }
    }
}
