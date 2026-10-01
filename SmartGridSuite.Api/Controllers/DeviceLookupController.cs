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
            CancellationToken cancellationToken,
            [FromQuery] DeviceLookupSearchType searchType = DeviceLookupSearchType.DeviceSerialNumber)
        {
            query = (query ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(
                    "Select a search type and enter its identifier.");
            }

            if (query.Length > 250)
            {
                return BadRequest(
                    "Device Lookup searches are limited to 250 characters.");
            }

            if (!Enum.IsDefined(searchType))
                return BadRequest("Select a valid Device Lookup search type.");

            var result =
                await _deviceLookupService.SearchAsync(
                    query,
                    searchType,
                    cancellationToken);

            return Ok(result);
        }
    }
}
