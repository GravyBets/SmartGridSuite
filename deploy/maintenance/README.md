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

## Step-by-step: set the restart password on the VM

These instructions are for when you are back on the work network. **Do not paste your password into chat,
GitHub, a command line, or appsettings.json.** The installer asks for it privately in the VM terminal.

### 1. Get the correct branch on your work computer

Open a PowerShell terminal in your SmartGridSuite repository (for example, Visual Studio's terminal).

```powershell
git status
```

If there are uncommitted changes, stop and preserve them before switching branches. Do not discard them.
When the working tree is clean:

```powershell
git fetch origin
git switch fix/dispatch-task-badge
git pull --ff-only origin fix/dispatch-task-badge
```

If Git reports a conflict, divergence, or a branch-switch error, stop rather than force the operation.
The setup files are in `deploy/maintenance` on this branch, not the unchanged release branch.

### 2. Copy the two setup files to the VM

Run these from the same PowerShell terminal, still at the repository root:

```powershell
ssh admin@10.130.206.135 "mkdir -p sgs-maintenance-setup && chmod 700 sgs-maintenance-setup"
scp .\deploy\maintenance\install-maintenance.sh .\deploy\maintenance\smartgridsuite-restart-api admin@10.130.206.135:sgs-maintenance-setup/
ssh admin@10.130.206.135
```

The SSH/SCP login is your existing VM login. It is separate from the new shared API restart password.
If SSH asks about an unfamiliar or changed host key, verify the VM identity before accepting it.

### 3. Check the VM prerequisites

You are now typing in the Linux VM terminal:

```bash
cd ~/sgs-maintenance-setup
getent passwd sgsapi
getent group sgsapi
ls -l /etc/init.d/smartgridsuite-api
sudo service smartgridsuite-api status
```

Confirm the account/group and service exist. The service should be the existing SmartGridSuite API.
If the service is missing, stopped unexpectedly, or the VM uses a different service name/account, stop here.
The helper is intended for the existing SysV service; do not substitute an unrelated service.

Normalize line endings on these uploaded copies in case Git checked them out as Windows CRLF:

```bash
sed -i 's/\r$//' install-maintenance.sh smartgridsuite-restart-api
bash -n install-maintenance.sh smartgridsuite-restart-api
```

The syntax check should finish without an error. This does not execute either script.

### 4. Install the helper and choose the password

```bash
sudo bash install-maintenance.sh
```

You may first get a sudo prompt for your **existing VM account password**.
The script then asks:

- `New shared API restart password (12-256 characters):`
- `Confirm password:`

Choose a unique restart-only password, 12–256 characters, and enter it twice.
**Nothing appears while you type—not even dots. This is normal.**
The new password is what authorized shop administrators will enter into the app's Restart API dialog.
It does not change any Linux, database, or company-account password.

Expected completion messages:

```text
Restart password verifier saved. No API restart was performed.
Setup complete. Deploy the updated API normally to load this feature.
```

If you get an error instead, stop and resolve it before proceeding. A failed installation may have already
installed the helper or sudoers rule, but password validation failure does not replace the existing password file.
Do not assume the new password was saved without the completion message.

The installer creates/updates:

| Location | Purpose |
| --- | --- |
| `/usr/local/sbin/smartgridsuite-restart-api` | Root-owned helper that restarts only the API service. |
| `/etc/sudoers.d/smartgridsuite-maintenance` | Allows sgsapi to invoke only that helper, with no arguments. |
| `/etc/smartgridsuite/maintenance.json` | Stores the enabled flag and salted password verifier, not the plaintext password. |

**This setup step does not restart the API and does not touch the database.**

### 5. Verify the setup WITHOUT triggering a restart

```bash
sudo /usr/sbin/visudo -c
sudo stat -c '%U:%G %a %n' /etc/smartgridsuite/maintenance.json /usr/local/sbin/smartgridsuite-restart-api /etc/sudoers.d/smartgridsuite-maintenance
sudo -u sgsapi test -r /etc/smartgridsuite/maintenance.json && echo "API account can read maintenance configuration"
sudo -l -U sgsapi
```

Expected ownership/modes:

- Maintenance JSON: `root:sgsapi 640`.
- Restart helper: `root:root 755`.
- Sudoers file: `root:root 440`.

The sudo listing should include the no-password permission for the exact helper path with an empty argument list.
Other existing permissions may also be listed. The permission listing does not execute the helper.
Do not run the helper itself as a verification command—that would queue a real restart.
Do not print or share the maintenance JSON; its password verifier is also sensitive.

### 6. Deploy before testing the button

Installing the password/helper alone does not add the new endpoint to the currently running API.
Deploy the updated **API and client** from this branch through the normal deployment process.
Keep the API address `http://10.130.206.135:7140/`; no certificate or Apache change is needed.

During a planned quiet window:

1. Open Administration → System Health and note the API start time.
2. Click Restart API and try a wrong password once. It should be rejected without restarting.
3. Click Restart API again and enter the shared password you chose in step 4.
   **This is the step that intentionally interrupts API service for everyone.**
4. Wait for “API restart confirmed” and a newer API start time. The client waits up to 90 seconds.
5. If it cannot confirm a restart, inspect the VM rather than repeatedly pressing Restart:

```bash
sudo service smartgridsuite-api status
sudo tail -n 50 /var/log/smartgridsuite-api-restart.log
```

If the helper has never run, its log may not exist yet.
A 404 from the button usually means the updated API was not deployed or the client is pointed at the wrong API.
“API restart has not been configured” means to check setup and the API's access to the maintenance configuration.
“Too many restart attempts” means wait a full minute before trying again.

### Change or forget the shared password later

Reconnect to the VM and rerun the installer:

```bash
cd ~/sgs-maintenance-setup
sudo bash install-maintenance.sh
```

It asks for a new password twice and replaces the old verifier; the old restart password is not required.
You still need VM sudo access. On the updated API, configuration reloads without an intentional service restart.
Rerunning the installer also sets RestartEnabled back to true. Share the new password only through an approved private channel.

### Disable the restart feature

On the VM, open the configuration locally:

```bash
sudo nano /etc/smartgridsuite/maintenance.json
```

Change only `"RestartEnabled": true` to `"RestartEnabled": false`, leaving valid JSON.
Save and exit. The updated API reloads the setting; the button stays visible but restart requests are refused.
Do not remove the password verifier or expose the file in screenshots.

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
