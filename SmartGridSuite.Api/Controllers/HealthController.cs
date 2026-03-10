using Microsoft.AspNetCore.Mvc;
using SmartGridSuite.Api.Data;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        public HealthController(SmartGridDbContext db) => _db = db;

        [HttpGet("db")]
        public async Task<IActionResult> Db()
            => Ok(new { canConnect = await _db.Database.CanConnectAsync() });
    }
}