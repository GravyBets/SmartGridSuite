global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SmartGridSuite.Api.Controllers;
using SmartGridSuite.Api.Services.SystemHealth;
using SmartGridSuite.Contracts.Administration;

// No API host, database, network, or real restart is used by these checks.
// Fixed verifier was independently generated using Python hashlib.pbkdf2_hmac.
const string verifier = "PBKDF2-SHA256:210000:AAECAwQFBgcICQoLDA0ODw==:sMrFaDwoP5mdOwWZuNn+EosToyDStRaYjBwBSmwAn2A=";
static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
Check(RestartPassword.Verify("offline-test-password", verifier), "valid PBKDF2 verifier");
Check(!RestartPassword.Verify("incorrect", verifier), "incorrect password rejected");
Check(!RestartPassword.Verify("offline-test-password ", verifier), "password not trimmed");
Check(!RestartPassword.Verify("", verifier), "empty password rejected");
Check(!RestartPassword.Verify("x", "broken"), "malformed verifier rejected");
Check(!RestartPassword.Verify("x", verifier.Replace("210000", "999999999")), "unbounded iteration count rejected");
Check(!RestartPassword.Verify(new string('x', 257), verifier), "oversized password rejected");

var disabled = new ConfigurationBuilder().Build();
var service = new ApiRestartService(disabled,
    new ApplicationRuntimeHealthService(disabled), NullLogger<ApiRestartService>.Instance);
Check((await service.RequestAsync("offline-test-password")).Status == 503,
    "restart disabled without VM configuration");

var controller = new AdminMaintenanceController(service)
{
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
};
controller.Request.Scheme = "http";
var result = await controller.Restart(new RestartApiRequest { Password = "offline-test-password" });
Check(result.Result is ObjectResult { StatusCode: 503 }, "HTTP reaches restart service but unconfigured restart stays disabled");

if (OperatingSystem.IsLinux())
{
    var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AdminMaintenance:RestartEnabled"] = "true",
        ["AdminMaintenance:RestartPasswordHash"] = verifier
    }).Build();
    var guarded = new ApiRestartService(enabled,
        new ApplicationRuntimeHealthService(enabled), NullLogger<ApiRestartService>.Instance);
    var httpController = new AdminMaintenanceController(guarded)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };
    httpController.Request.Scheme = "http";
    // Deliberately never supply a correct password to the enabled service.
    // Empty/wrong passwords must be rejected by the server even over HTTP.
    for (var i = 0; i < 5; i++)
    {
        var rejected = await httpController.Restart(new RestartApiRequest
        {
            Password = i == 0 ? "" : "incorrect"
        });
        Check(rejected.Result is ObjectResult { StatusCode: 401 },
            "HTTP missing/invalid password rejected " + (i + 1));
    }
    var limited = await httpController.Restart(new RestartApiRequest { Password = "incorrect" });
    Check(limited.Result is ObjectResult { StatusCode: 429 }, "HTTP attempt limit enforced");
}
Console.WriteLine("Offline checks complete. No restart helper was invoked.");
