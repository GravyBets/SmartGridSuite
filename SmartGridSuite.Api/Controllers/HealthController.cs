using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using System.Linq;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/health")]
    public sealed class HealthController : ControllerBase
    {
        private readonly SmartGridDbContext _db;

        public HealthController(SmartGridDbContext db)
        {
            _db = db;
        }

        // Separately verifies EF model creation and the physical database
        // connection so development failures can be diagnosed precisely.
        [HttpGet]
        public async Task<IActionResult> GetHealth(
            CancellationToken ct)
        {
            int mappedEntityCount;

            try
            {
                /*
                 * Accessing Model forces EF Core to build and validate the
                 * complete entity model before attempting a database connection.
                 */
                mappedEntityCount =
                    _db.Model.GetEntityTypes().Count();
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    apiAvailable = true,
                    databaseAvailable = false,
                    failureStage = "Entity Framework model creation",
                    errorType = ex.GetType().FullName,
                    errorMessage = ex.Message,
                    innerErrorType = ex.InnerException?.GetType().FullName,
                    innerErrorMessage = ex.InnerException?.Message,
                    checkedAtUtc = DateTimeOffset.UtcNow
                });
            }

            try
            {
                /*
                 * OpenConnectionAsync throws the real connection error instead
                 * of reducing every failure to the boolean returned by CanConnectAsync.
                 */
                await _db.Database.OpenConnectionAsync(ct);

                await _db.Database.CloseConnectionAsync();

                return Ok(new
                {
                    apiAvailable = true,
                    databaseAvailable = true,
                    failureStage = (string?)null,
                    errorType = (string?)null,
                    errorMessage = (string?)null,
                    mappedEntityCount,
                    checkedAtUtc = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    apiAvailable = true,
                    databaseAvailable = false,
                    failureStage = "Database connection",
                    errorType = ex.GetType().FullName,
                    errorMessage = ex.Message,
                    innerErrorType = ex.InnerException?.GetType().FullName,
                    innerErrorMessage = ex.InnerException?.Message,
                    mappedEntityCount,
                    checkedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        // Preserves the shorter database-only endpoint while still returning
        // enough development information to diagnose connection failures.
        [HttpGet("db")]
        public async Task<IActionResult> GetDatabaseHealth(
            CancellationToken ct)
        {
            try
            {
                await _db.Database.OpenConnectionAsync(ct);
                await _db.Database.CloseConnectionAsync();

                return Ok(new
                {
                    canConnect = true,
                    errorType = (string?)null,
                    errorMessage = (string?)null
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    canConnect = false,
                    errorType = ex.GetType().FullName,
                    errorMessage = ex.Message,
                    innerErrorType = ex.InnerException?.GetType().FullName,
                    innerErrorMessage = ex.InnerException?.Message
                });
            }
        }
    }
}