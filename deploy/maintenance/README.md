# HTTP API restart and Parent DB testing

The client uses the existing API address: `http://10.130.206.135:7140/`.
No certificate, Apache HTTPS configuration, new port, or client trust installation is required.
Nothing in this branch has been deployed or run against production.

## Security limitation

HTTP sends the shared restart password unencrypted. A party able to observe or alter the connection can capture/reuse it.
This feature is an access check against callers who do not know the password, not protection against network interception.
Use a unique restart-only password, never a company-account password. Do not expose the API publicly.
The client does not save the password or follow credential-bearing redirects.
The server keeps a salted PBKDF2 verifier, limits attempts, and permits only the fixed restart helper.
Swagger callers must supply the same password. No password belongs in Git or chat.

## One-time VM setup and rollout

Prepared for the existing MX Linux SysV service `smartgridsuite-api` running as `sgsapi`.

1. Build the API/client and run the offline checks below. Do not launch a development API against the production database.
2. Inspect the existing init script to confirm its service/account and restart behavior.
   The detached helper is designed for SysV, not a systemd cgroup that kills descendants.
3. Copy this maintenance folder to the VM and run `sudo bash install-maintenance.sh` from that folder.
   This installs the root-owned helper and a sudoers rule granting only that exact command with no arguments.
   The script prompts locally for the shared password and saves only its salted verifier in
   `/etc/smartgridsuite/maintenance.json` (root-owned, group-readable by sgsapi).
   **Setup does not restart the API.**
4. Deploy the updated API using your existing deployment workflow, then test and publish the client.
   Both API and client updates are needed. Keep the existing HTTP listener, port 7140, and ClickOnce URLs unchanged.
5. At a planned quiet time, test a wrong password first (must not restart), then a correct one.
   Confirm the displayed API start time changes and health refreshes.
   Test the Parent DB button without intentionally disrupting production connectivity.

The helper and configuration live outside API deployment swaps.
Rerun the installer to rotate the password. The running API reloads the maintenance JSON.
Set `AdminMaintenance.RestartEnabled` to false there to disable restart without reverting the client.

## Restart behavior

The always-visible button opens a password/confirmation dialog, posts to `api/admin/restart-api`,
and waits up to 90 seconds for a newer API process start time. HTTP 202 means queued, not completed.
The detached helper waits five seconds before restarting the fixed service; a lock and cooldown prevent overlapping requests.
The password is checked server-side before the helper can run. Missing configuration fails closed.
If the API is completely down, its endpoint cannot answer; restart through the VM instead.
Logs: existing API logs and `/var/log/smartgridsuite-api-restart.log`. The password is not logged.

## Parent DB test

POST `api/admin/system-health/test-parent-db` tests a fresh, non-pooled SQL connection and read-only `SELECT 1`.
Both success and failure update shared Parent DB health and return a fresh health snapshot for all cards in the view.
Historical last-failure information remains after recovery; unavailable-since clears after success.
An API/network error marks the displayed Parent DB result unverified and other values stale.
This does not refresh the inventory cache or reload site data already open elsewhere.

## Verification

Safe offline checks with .NET 8 SDK (no network/API/database access during execution):

```powershell
dotnet run --project tests/MaintenanceOfflineChecks/MaintenanceOfflineChecks.csproj
dotnet build SmartGridSuite.Api/SmartGridSuite.Api.csproj
dotnet build SmartGridSuite.Client/SmartGridSuite.Client.csproj
```

The offline checks cover password verification, an unconfigured restart failing closed over HTTP,
and (on Linux) missing/wrong passwords and rate limiting through the HTTP controller.
No enabled service is ever given a valid password by these checks, so they cannot restart it.
HTTP model validation and successful restart integration still need runtime verification.

Authoring checks: source inspection and XML parsing. The .NET SDK/Windows are unavailable here,
so builds and live WPF/service checks remain pending. No production calls or restarts were made.
The earlier unused Apache HTTPS template was removed; it remains recoverable from Git history.
