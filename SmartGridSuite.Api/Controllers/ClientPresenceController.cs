#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Administration.ConnectedClients;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/client-presence")]
    public sealed class ClientPresenceController : ControllerBase
    {
        private static readonly TimeSpan OnlineThreshold =
            TimeSpan.FromMinutes(3);

        private readonly SmartGridDbContext _db;
        private readonly ClientVersionOptions _versionOptions;

        public ClientPresenceController(
            SmartGridDbContext db,
            IOptions<ClientVersionOptions> versionOptions)
        {
            _db = db;
            _versionOptions = versionOptions.Value;
        }

        /*
         * Called periodically by each running SmartGridSuite client.
         *
         * The server owns the LastSeen timestamp. We intentionally do not
         * trust the laptop's local clock for presence calculations.
         */
        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat(
            [FromBody] ClientHeartbeatRequest request,
            CancellationToken ct)
        {
            request ??= new ClientHeartbeatRequest();

            var machineName =
                CleanRequired(
                    request.MachineName,
                    maxLength: 128);

            if (string.IsNullOrWhiteSpace(machineName))
            {
                return BadRequest(
                    "MachineName is required.");
            }

            var employeeId =
                CleanNullable(
                    request.EmployeeId,
                    maxLength: 32);

            var displayName =
                CleanNullable(
                    request.DisplayName,
                    maxLength: 128);

            var windowsUser =
                CleanNullable(
                    request.WindowsUser,
                    maxLength: 128);

            var clientVersion =
                CleanRequired(
                    request.ClientVersion,
                    maxLength: 32);

            var currentModule =
                CleanRequired(
                    request.CurrentModule,
                    maxLength: 64);

            if (string.IsNullOrWhiteSpace(clientVersion))
                clientVersion = "Unknown";

            if (string.IsNullOrWhiteSpace(currentModule))
                currentModule = "Module Launcher";

            var nowUtc =
                DateTime.UtcNow;

            var presence =
                await _db.ClientPresence
                    .FirstOrDefaultAsync(
                        x => x.MachineName == machineName,
                        ct);

            if (presence == null)
            {
                presence =
                    new ClientPresenceEntity
                    {
                        MachineName = machineName,

                        EmployeeId = employeeId,
                        DisplayName = displayName,
                        WindowsUser = windowsUser,

                        ClientVersion = clientVersion,
                        CurrentModule = currentModule,

                        FirstSeenAtUtc = nowUtc,
                        LastSeenAtUtc = nowUtc
                    };

                _db.ClientPresence.Add(presence);
            }
            else
            {
                presence.EmployeeId =
                    employeeId;

                presence.DisplayName =
                    displayName;

                presence.WindowsUser =
                    windowsUser;

                presence.ClientVersion =
                    clientVersion;

                presence.CurrentModule =
                    currentModule;

                presence.LastSeenAtUtc =
                    nowUtc;
            }

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        /*
         * Returns the current and recently-seen SmartGridSuite clients.
         *
         * Online and outdated state are calculated at request time rather
         * than persisted so the database never contains stale status flags.
         */
        [HttpGet]
        [ResponseCache(
            NoStore = true,
            Location = ResponseCacheLocation.None)]
        public async Task<ActionResult<ConnectedClientsResponse>>
            GetConnectedClients(
                CancellationToken ct)
        {
            var nowUtc =
                DateTime.UtcNow;

            var onlineCutoffUtc =
                nowUtc.Subtract(
                    OnlineThreshold);

            var latestVersion =
                (_versionOptions.LatestVersion ?? string.Empty)
                    .Trim();

            var rows =
                await _db.ClientPresence
                    .AsNoTracking()
                    .OrderByDescending(
                        x => x.LastSeenAtUtc)
                    .ToListAsync(ct);

            var clients =
                rows
                    .Select(row =>
                    {
                        var isOnline =
                            row.LastSeenAtUtc >=
                            onlineCutoffUtc;

                        var isOutdated =
                            IsVersionOutdated(
                                row.ClientVersion,
                                latestVersion);

                        return new ConnectedClientDto
                        {
                            Id =
                                row.Id,

                            EmployeeId =
                                row.EmployeeId ?? "",

                            DisplayName =
                                row.DisplayName ?? "",

                            WindowsUser =
                                row.WindowsUser ?? "",

                            MachineName =
                                row.MachineName,

                            ClientVersion =
                                row.ClientVersion,

                            CurrentModule =
                                row.CurrentModule,

                            FirstSeenAtUtc =
                                row.FirstSeenAtUtc,

                            LastSeenAtUtc =
                                row.LastSeenAtUtc,

                            IsOnline =
                                isOnline,

                            IsOutdated =
                                isOutdated
                        };
                    })
                    .OrderByDescending(x =>
                        x.IsOnline)
                    .ThenBy(x =>
                        x.DisplayName)
                    .ThenBy(x =>
                        x.MachineName)
                    .ToList();

            /*
             * Summary cards describe clients that are actually online.
             * An old laptop that has not checked in for three weeks should
             * not count as an actively used outdated version.
             */
            var onlineClients =
                clients
                    .Where(x => x.IsOnline)
                    .ToList();

            var response =
                new ConnectedClientsResponse
                {
                    ServerTimeUtc =
                        nowUtc,

                    LatestVersion =
                        latestVersion,

                    OnlineClientCount =
                        onlineClients.Count,

                    OutdatedClientCount =
                        onlineClients.Count(
                            x => x.IsOutdated),

                    VersionsInUseCount =
                        onlineClients
                            .Select(x =>
                                x.ClientVersion)
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .Count(),

                    Clients =
                        clients
                };

            return Ok(response);
        }

        private static bool IsVersionOutdated(
            string? clientVersion,
            string? latestVersion)
        {
            var current =
                (clientVersion ?? string.Empty)
                    .Trim();

            var latest =
                (latestVersion ?? string.Empty)
                    .Trim();

            if (string.IsNullOrWhiteSpace(latest))
                return false;

            if (string.IsNullOrWhiteSpace(current))
                return true;

            /*
             * SmartGridSuite currently uses numeric versions such as
             * 3.1.0.5, which System.Version handles correctly.
             */
            if (Version.TryParse(
                    current,
                    out var parsedCurrent) &&
                Version.TryParse(
                    latest,
                    out var parsedLatest))
            {
                return parsedCurrent <
                       parsedLatest;
            }

            /*
             * If a future/nonstandard version cannot be parsed, treat
             * anything other than an exact match as out of date so the
             * Admin screen calls attention to it.
             */
            return !string.Equals(
                current,
                latest,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanRequired(
            string? value,
            int maxLength)
        {
            var clean =
                (value ?? string.Empty)
                    .Trim();

            if (clean.Length <= maxLength)
                return clean;

            return clean[..maxLength];
        }

        private static string? CleanNullable(
            string? value,
            int maxLength)
        {
            var clean =
                (value ?? string.Empty)
                    .Trim();

            if (string.IsNullOrWhiteSpace(clean))
                return null;

            if (clean.Length > maxLength)
                clean = clean[..maxLength];

            return clean;
        }
    }
}