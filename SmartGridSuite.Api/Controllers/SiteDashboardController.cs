using Microsoft.AspNetCore.Mvc;
using SmartGridSuite.Api.Mappings;
using SmartGridSuite.Api.Services.ParentSync;
using SmartGridSuite.Api.Services.ParentSync.Models;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Net;
using System.Net.Sockets;
using SmartGridSuite.Api.Services.SiteDashboard;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/site-dashboard")]    
    public sealed class SiteDashboardController : ControllerBase
    {
        private readonly ParentSyncService _parentSyncService;

        private readonly SiteDashboardLookupService _siteDashboardLookupService;

        public SiteDashboardController(
             ParentSyncService parentSyncService,
             SiteDashboardLookupService siteDashboardLookupService)
        {
            _parentSyncService = parentSyncService;
            _siteDashboardLookupService = siteDashboardLookupService;
        }

        [HttpGet("site-count")]
        public async Task<ActionResult<object>> GetSiteCount(CancellationToken cancellationToken)
        {
            var count = await _parentSyncService.GetSiteCountAsync(cancellationToken);
            return Ok(new { siteCount = count });
        }

        //Which model to use (AMS, DACs, IG...)
        [HttpGet("site-dashboard-route/{siteId}")]
        public async Task<ActionResult<SiteDashboardRouteInfoDto>> GetSiteDashboardRouteInfo(
            string siteId, CancellationToken cancellationToken = default)
        {
            var normalizedSiteId = NormalizeRequiredText(siteId);

            if (normalizedSiteId is null)
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetSiteDashboardRouteInfoAsync(normalizedSiteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{normalizedSiteId}' was not found in sgc_main.Site." });
            }

            return Ok(row.ToDto());
        }

        [HttpGet("{siteId}")]
        public async Task<ActionResult<SiteDashboardResponseDto>> GetSiteDashboard(
            string siteId, CancellationToken cancellationToken = default)
        {
            var normalizedSiteId =
                NormalizeRequiredText(siteId);

            if (normalizedSiteId is null)
            {
                return BadRequest("Site ID is required.");
            }

            var result =
                await _siteDashboardLookupService.GetAsync(
                    normalizedSiteId,
                    cancellationToken);

            switch (result.State)
            {
                case SiteDashboardLookupState.Live:
                case SiteDashboardLookupState.Cached:
                    {
                        if (result.Dashboard is null)
                        {
                            return StatusCode(
                                StatusCodes.Status500InternalServerError,
                                new
                                {
                                    message =
                                        "The Site Dashboard lookup completed without dashboard data."
                                });
                        }

                        return Ok(
                            result.Dashboard.ToDto());
                    }

                case SiteDashboardLookupState.NotFound:
                    {
                        return NotFound(
                            new
                            {
                                message =
                                    $"Site '{normalizedSiteId}' was not found or is not yet supported."
                            });
                    }

                case SiteDashboardLookupState.ParentDatabaseUnavailable:
                    {
                        var problem =
                            new ProblemDetails
                            {
                                Status =
                                    StatusCodes.Status503ServiceUnavailable,

                                Title =
                                    "Parent database unavailable",

                                Detail =
                                    "The Parent database is unavailable and no cached site information exists."
                            };

                        problem.Extensions["code"] =
                            "PARENT_DB_UNAVAILABLE";

                        problem.Extensions["siteId"] =
                            normalizedSiteId;

                        return StatusCode(
                            StatusCodes.Status503ServiceUnavailable,
                            problem);
                    }

                default:
                    {
                        return StatusCode(
                            StatusCodes.Status500InternalServerError,
                            new
                            {
                                message =
                                    "An unexpected Site Dashboard lookup result occurred."
                            });
                    }
            }
        }

        //IP Look Up
        [HttpGet("associated-site-by-ip")]
        public async Task<ActionResult<AssociatedSiteByIpLookupDto>> GetAssociatedSiteByIp(
            [FromQuery] string ip,
            CancellationToken cancellationToken = default)
        {
            var normalizedIp = NormalizeRequiredText(ip);

            if (normalizedIp is null)
                return BadRequest("IP address is required.");

            if (!IPAddress.TryParse(normalizedIp, out var parsedIp) ||
                parsedIp.AddressFamily != AddressFamily.InterNetwork)
            {
                return BadRequest("Enter a valid IPv4 address.");
            }

            var result = await _siteDashboardLookupService.FindAssociatedSiteByIpAsync(
                normalizedIp,
                cancellationToken);

            return Ok(result);
        }

        //AMS

        [HttpGet("ams-mr-site/{siteId}")]
        public async Task<ActionResult<AmsSiteDashboardDto>> GetAmsMrSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            var normalizedSiteId = NormalizeRequiredText(siteId);

            if (normalizedSiteId is null)
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetAmsMrSiteAsync(normalizedSiteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{normalizedSiteId}' was not found in sgc_comm.AMS." });
            }

            return Ok(row.ToDto());
        }


        //DACs
        [HttpGet("dacs-site/{siteId}")]
        public async Task<ActionResult<DacsSiteDashboardDto>> GetDacsSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            var normalizedSiteId = NormalizeRequiredText(siteId);

            if (normalizedSiteId is null)
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetDacsSiteAsync(normalizedSiteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{normalizedSiteId}' was not found or is not a DACS site." });
            }

            return Ok(row.ToDto());
        }

        //IG
        [HttpGet("igsd-site/{siteId}")]
        public async Task<ActionResult<IgsdSiteDashboardDto>> GetIgsdSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            var normalizedSiteId = NormalizeRequiredText(siteId);

            if (normalizedSiteId is null)
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetIgsdSiteAsync(normalizedSiteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{normalizedSiteId}' was not found in sgc_comm.IGSD." });
            }

            return Ok(row.ToDto());
        }

        //Range Extenders
        [HttpGet("rx-site/{siteId}")]
        public async Task<ActionResult<RxSiteDashboardDto>> GetRxSite(
            string siteId, CancellationToken cancellationToken = default)
        {
            var normalizedSiteId = NormalizeRequiredText(siteId);

            if (normalizedSiteId is null)
            {
                return BadRequest("Site ID is required.");
            }

            var row = await _parentSyncService.GetRxSiteAsync(normalizedSiteId, cancellationToken);

            if (row is null)
            {
                return NotFound(new { message = $"Site '{normalizedSiteId}' was not found in sgc_comm.RE." });
            }

            return Ok(row.ToDto());
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
        public async Task<ActionResult<IReadOnlyList<TowerSearchRow>>>SearchTowers(
            [FromQuery] string term,
            [FromQuery] int take = 25,
            CancellationToken cancellationToken = default)
        {
            var normalizedTerm =
                NormalizeRequiredText(term);

            if (normalizedTerm is null)
            {
                return BadRequest(
                    "Search term is required.");
            }

            var result =
                await _siteDashboardLookupService.SearchTowersAsync(
                    normalizedTerm,
                    take,
                    cancellationToken);

            return Ok(result.Rows);
        }

        [HttpGet("tower-dashboard/{topNameId:int}")]
        public async Task<ActionResult<SiteDashboardResponseDto>>GetTowerDashboard(
            int topNameId, CancellationToken cancellationToken = default)
        {
            if (topNameId <= 0)
            {
                return BadRequest(
                    "TopNameId must be greater than zero.");
            }

            var result =
                await _siteDashboardLookupService.GetTowerAsync(
                    topNameId,
                    cancellationToken);

            switch (result.State)
            {
                case SiteDashboardLookupState.Live:
                case SiteDashboardLookupState.Cached:
                    {
                        if (result.Dashboard is null)
                        {
                            return StatusCode(
                                StatusCodes.Status500InternalServerError,
                                new
                                {
                                    message =
                                        "The tower lookup completed without dashboard data."
                                });
                        }

                        return Ok(
                            result.Dashboard.ToDto());
                    }

                case SiteDashboardLookupState.NotFound:
                    return NotFound(
                        new
                        {
                            message =
                                $"Tower '{topNameId}' was not found."
                        });

                case SiteDashboardLookupState
                    .ParentDatabaseUnavailable:
                default:
                    {
                        var problem =
                            new ProblemDetails
                            {
                                Status =
                                    StatusCodes
                                        .Status503ServiceUnavailable,

                                Title =
                                    "Parent database unavailable",

                                Detail =
                                    "The Parent database is unavailable, " +
                                    "and no cached tower information exists."
                            };

                        problem.Extensions["code"] =
                            "PARENT_DB_UNAVAILABLE";

                        return StatusCode(
                            StatusCodes.Status503ServiceUnavailable,
                            problem);
                    }
            }
        }
                
        private static string? NormalizeRequiredText(string? value)
        {
            var normalized = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }
}