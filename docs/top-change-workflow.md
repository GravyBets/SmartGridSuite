# TOP Change workflow

## Where to find it

Open an existing ticket's Site Dashboard. **Request TOP Change** appears below the
**TOP Access** heading for MR (AMS), DAC and IG sites. Select the new TOP, its sector,
and whether the radio change is planned or already done. Current TOP, sector and IP
remain visible separately. The request sets the ticket to protected **TOP Change** status.
This version requires a ticket; opening a site without one prompts you to open a ticket first.

In **Dispatch → Tasks**, select that ticket and click **TOP Change**:

- MR: enter the assigned IP and select **Save IP / Notify Technician**.
- DAC/IG: **Email IP Request** sends the old/new details to the configured team.
  When they respond, Dispatch enters the returned IP with the same Save button.
- Saving the IP makes **New IP Ready** appear in the Site Dashboard (refreshes every
  30 seconds while open). Field Technician Tasks show the IP in the status column on
  their next refresh. The requester also receives email when email is enabled and
  their Technician record has an email address. The dialog displays the email outcome.
- Email failure does not undo a saved IP. Re-saving the same IP retries the notification;
  it may send a duplicate email if the prior result was uncertain. A different IP cannot
  overwrite an existing assignment in this version.
- There is no separate technician acknowledgement button. A normal write-up submitted
  after IP assignment finishes the request and follows the configured write-up workflow.
  Earlier write-ups are saved, but keep TOP Change status and active daily assignments.

The request is stored separately from Dispatch Notes. It does not edit Parent DB or
configure radio equipment. TOP/sector choices come from the tower cache; cached current
site values are labelled in the dialog. Verify IP allocation using your existing process;
the app checks IPv4 format, not availability, subnet membership or duplicate allocation.

## Setup on the VM

1. Back up the SmartGridSuite application database. Build this branch's API and Windows
   client before replacing either installed version. Do not start the API against a
   production database to run the offline checks below.
2. In your MariaDB SQL tool, select the **SmartGridSuite application database** (the one
   in the API connection string). Run [`sql/top-change-setup.sql`](sql/top-change-setup.sql).
   It adds `ticket_top_changes` and the TOP Change status. Run it **before deploying the
   API** because ticket saves now consult this table. It does not modify Parent DB.
   Existing ticket-status schema updates from this branch must already be installed.
3. Add this section to the API's deployed `appsettings.Production.json` (merge with its
   existing contents). Replace the example with the actual IP-assignment team's addresses:

   ```json
   "TopChange": {
     "IpRequestRecipients": "ip-team@example.com;second-person@example.com"
   }
   ```

   Alternatively, set `TopChange__IpRequestRecipients` in the API service environment.
   This is one recipient list for both DAC and IG requests. Never use the example addresses.
4. Confirm existing SMTP settings and each requesting technician's email address.
   Existing Email Enabled, Dry Run and Test Recipient Override settings apply to TOP
   emails. For initial tests use Dry Run or your test recipient; a suppressed/dry-run
   result is not delivery to the real team. No email is sent merely by opening the dialog.
5. Deploy/restart the API using your existing procedure, then update the Windows client
   from the same branch. HTTP and the existing API access model are unchanged. Refresh
   the tower cache if the TOP/sector lists are empty or stale.

## Verification

The editing environment had no .NET SDK, Windows UI, company network or database access.
Only C# syntax parsing, XAML XML parsing and source review were performed here.
The following .NET checks and integration scenarios still require execution.

From the repository root on a machine with .NET 8:

```powershell
dotnet run --project tests/TopChangeOfflineChecks/TopChangeOfflineChecks.csproj
dotnet build SmartGridSuite.Api/SmartGridSuite.Api.csproj
dotnet build SmartGridSuite.Client/SmartGridSuite.Client.csproj
```

The offline checks link only workflow/DTO source. They do not start the API or connect
to a database. API/client builds also do not run the app. Use Windows for the WPF build.

Use a test database/ticket for these integration checks:

1. MR, DAC and IG: check pulled old TOP/sector/IP, filtered new sectors, both field-work
   options, unavailable cache warning and unsupported site types.
2. Submit a request; verify exactly one active row, Dispatch Tasks visibility, technician
   status, and preserved old values. Retry after a dropped response; no duplicate request.
3. Open the same ticket on two clients and submit simultaneously. Only one active request
   should succeed. Review conflicts without losing the existing request.
4. Submit a normal write-up before assigning an IP. Confirm narrative/history saved, TOP
   Change retained, and daily assignment still active. Try Edit Ticket, bulk status change,
   SAP import, assignment publish/unassign and Dispatch Close; an active request's status
   must remain TOP Change. Edit/Close should explain the protection.
5. DAC/IG: test missing recipients, Dry Run, test-recipient email and normal delivery after
   verifying the real recipient list. MR should not offer Email IP Request.
6. Assign an IP. Check invalid input rejection, new IP in the technician's dashboard/tasks,
   and requester email. Test disabled email, missing technician email and SMTP failure;
   the assigned IP must remain saved and available in-app.
7. Submit another normal write-up after the IP is ready. Confirm request becomes Completed,
   active marker clears, normal write-up target status applies, and assignments complete.
   Repeat a write-up retry and verify existing submission-id protection still works.
8. Simultaneously assign IP and submit a write-up. A write-up started before the IP assignment
   must leave the request active; a later write-up completes it. Retry any transaction conflict.
9. Navigate between sites while status refresh is running; no IP should appear on the wrong
   ticket. Check the TOP Access card and modal on a small laptop and in both themes.

## Rollback

The feature is isolated on `feature/dispatch-top-change-workflow`. Returning to another
branch restores that branch's code, but does not undo database changes. Before deploying
older code, finish active TOP changes and confirm this query returns no rows:

```sql
SELECT Id, TicketId, Site, State FROM ticket_top_changes WHERE ActiveTicketId IS NOT NULL;
```

Keep the additive table for request history. Older APIs do not enforce TOP Change protection;
do not switch them in while requests are active. Do not drop the table while this API is running.
There is no cancellation/reassignment screen in this first version; if a request is wrong,
stop and resolve it before recording an IP or completing its write-up.
