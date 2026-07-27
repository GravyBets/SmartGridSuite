using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Contracts.Versioning;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/system/client-version")]
    public sealed class ClientVersionController : ControllerBase
    {
        private readonly ClientVersionOptions _options;

        public ClientVersionController(IOptions<ClientVersionOptions> options)
        {
            _options = options.Value;
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(ClientVersionDto), StatusCodes.Status200OK)]
        public ActionResult<ClientVersionDto> GetClientVersion()
        {
            return Ok(new ClientVersionDto
            {
                LatestVersion =
                    _options.LatestVersion,

                MinimumSupportedVersion =
                    _options.MinimumSupportedVersion,

                PublishedAtUtc =
                    _options.PublishedAtUtc,

                InstallUrl =
                    _options.InstallUrl,

                ReleaseNotes =
                    _options.ReleaseNotes.ToArray()
            });
        }
    }
}