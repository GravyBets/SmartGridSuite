# HTTPS, API restart, and Parent DB testing

Prepared for the existing MX Linux VM, Apache, service `smartgridsuite-api`, and OS account `sgsapi`.
Nothing here has been installed or executed on production. The test branch now uses
`https://10.130.206.135/`. Do not distribute this client until HTTPS and certificate trust work on a work laptop.

## Certificate prerequisite

Obtain a server certificate with Subject Alternative Name **IP:10.130.206.135**, or choose an internal DNS name and update both Apache ServerName and ClientAppSettings.ApiBaseUrl to that name.
The issuing CA must be trusted by each laptop running SmartGridSuite. Your company CA is the preferred source if available.
A locally issued/self-signed certificate requires deliberate trust installation on each laptop (and may need IT approval); never disable certificate validation.
Do not send the private key or shared restart password to GitHub or chat.

Install the server full chain at `/etc/ssl/certs/smartgridsuite-fullchain.pem` and private key at
`/etc/ssl/private/smartgridsuite.key` (root-owned, private key mode 0600).
Certificate issuance/trust setup is not automated because the organization's CA and device policy are not known.

## Work-site rollout order

1. Build API and client from this branch. Run offline checks below. Do not launch a development API against the production database for testing.
2. Inspect the existing SysV init script. Confirm it runs the API as `sgsapi` and restarts only that service.
   The helper assumes SysV process supervision, not a systemd cgroup that kills descendant processes.
3. Copy this maintenance folder to the VM. Run `sudo bash install-maintenance.sh` from that folder.
   It validates a narrowly scoped sudoers rule and prompts locally for a shared password.
   Only a salted PBKDF2-SHA256 verifier is saved in the root-owned, group-readable
   `/etc/smartgridsuite/maintenance.json`. No API restart occurs during setup.
   Rerunning setup rotates the password; the API reloads this file.
4. Deploy the updated API using the existing reviewed deployment workflow. The maintenance file/helper live outside the deployment folder.
5. Install the trusted certificate and Apache configuration:

   ```bash
   sudo a2enmod ssl proxy proxy_http headers
   sudo install -o root -g root -m 0644 smartgridsuite-https.conf /etc/apache2/sites-available/smartgridsuite-https.conf
   sudo a2ensite smartgridsuite-https
   sudo apache2ctl configtest
   ```

   Only after configuration validation succeeds, reload Apache with `sudo service apache2 reload`.
   Confirm TCP 443 is reachable under the existing network/firewall policy. No firewall rules are changed by these files.
6. Verify `https://10.130.206.135/api/admin/system-health` from a work laptop without bypassing certificate validation.
   First verify the certificate and read-only health request; do not test restart during active field work.
7. Test the updated client, then publish it. Existing HTTP clients can continue during migration.
   After all clients have migrated, update the existing service's listen configuration to
   `http://127.0.0.1:7140` and verify Apache still works. Do not close the old API listener before old clients are upgraded.
   This final listener migration is manual because the actual init script is not in the repository.
   The restart endpoint already rejects direct HTTP and only trusts HTTPS forwarding from loopback.
8. Keep the ClickOnce update URL unchanged during this first API migration.
   Changing an existing ClickOnce installation/update URL is a separate rollout; the Apache config also serves existing `/install/` files over HTTPS.

Rollback: use the previous published client if HTTPS is not ready. Disable the Apache site and reload only after
checking remaining HTTPS consumers. Set `AdminMaintenance.RestartEnabled` to false in the VM maintenance JSON
to disable restart requests without reverting the application. Do not remove the existing HTTP listener during initial testing.

## Restart behavior

The button stays visible. It prompts for the shared password each time and does not save it.
The client refuses HTTP and refuses redirects for this credential-bearing request. The API independently requires HTTPS,
checks the password, limits attempts, and queues only the fixed root-owned helper.
Swagger callers need the same password. A shared password identifies a group, not an individual user.

The helper uses a separate session, lock, and cooldown, waits five seconds, then calls the fixed SysV service restart.
HTTP 202 means queued, not completed. The client confirms a newer process start time within 90 seconds.
If the API is completely down, its restart endpoint cannot answer: use the VM's service command.
Logs are in `/var/log/smartgridsuite-api-restart.log` and the existing API logs; the password is not logged.

## Parent DB test behavior

POST `/api/admin/system-health/test-parent-db` opens a fresh, non-pooled SQL connection using the existing configured identity
and executes read-only `SELECT 1` with a five-second bound. It updates shared Parent DB health on success/failure,
then returns a new full health snapshot. The view refreshes all its cards, data-source label, timestamps, and failure details.
Last-failure fields remain historical after recovery; unavailable-since clears on success.
The result says whether the actual probe succeeded, rather than equating HTTP 200 with database success.

An API/network failure marks the displayed Parent DB state unverified and the remaining values stale.
The test does not refresh the inventory cache or reload existing site data in other panes.

## Verification

Available here: shell syntax checks, XAML/XML parsing, source checks. No production requests were made.
.NET SDK and Windows were unavailable in the authoring environment; the C# build and WPF/runtime checks remain required.

Safe offline checks with a .NET 8 SDK, no database or API access:

```powershell
dotnet run --project tests/MaintenanceOfflineChecks/MaintenanceOfflineChecks.csproj
dotnet build SmartGridSuite.Api/SmartGridSuite.Api.csproj
dotnet build SmartGridSuite.Client/SmartGridSuite.Client.csproj
```

On the VM/work laptop, verify missing/wrong password rejection, a successful scheduled restart and new start time,
no repeated restart on polling, a successful Parent DB test, and updated failure indicators using an isolated test configuration.
Do not intentionally break production database connectivity to manufacture a failure.
Also test navigating away while a maintenance operation is pending and reconnecting after it.

References: [Apache reverse proxy documentation](https://httpd.apache.org/docs/2.4/mod/mod_proxy.html)
and [ASP.NET Core trusted proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
