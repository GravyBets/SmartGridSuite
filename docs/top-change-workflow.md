# TOP Change workflow

## Where to find it

Open an existing ticket's Site Dashboard. **Request TOP Change** spans the TOP Access card below the
TOP IPs (with Active Test beside the title) for MR (AMS), DAC and IG sites. Select the new TOP, its sector,
and whether the radio change is planned or already done. Current TOP, sector and IP
remain visible separately. The request sets the ticket to protected **TOP Change** status.
This version requires a ticket; opening a site without one prompts you to open a ticket first.

In **Dispatch → Tasks**, expand that ticket and use **View Request / Assign IP** in its
**TOP Change** section. The action uses the expanded ticket, independently of which row
is selected. The Dispatch window opens at 960 × 850 (limited by available screen space)
and can be resized; its sizing is defined in `TopChangeWindow.xaml`.


- MR, DAC and IG: use **Copy Request Details** to copy the site, ticket, old/new TOP
  and sector, current IP, requester and field-work status. Dispatch can paste this into
  their usual email process. The app does not create or send IP-change emails.
- Enter the assigned IP and select **Save Assigned IP**. The saved details remain visible
  so Dispatch can copy them again, including the new IP.
- Saving the IP makes **New IP Ready** appear in the Site Dashboard (refreshes every
  30 seconds while open). Field Technician Tasks show the IP on their next refresh.
  No IP-ready email is sent. Existing assignment/write-up emails are unchanged.
- A different IP cannot overwrite an existing assignment in this version.
- There is no separate technician acknowledgement button. A normal write-up submitted
  after IP assignment finishes the request and follows the configured write-up workflow.
  Earlier write-ups are saved, but keep TOP Change status and active daily assignments.

The request is stored separately from Dispatch Notes. It does not edit Parent DB or
configure radio equipment. TOP/sector choices come from the tower cache; cached current
site values are labelled in the dialog. Verify IP allocation using your existing process;
the app checks IPv4 format, not availability, subnet membership or duplicate allocation.

## Reviewing current site values

For a new request, the current TOP and sector are dropdowns, preselected to the site
lookup values. Current IP is editable using the ModernTextBox control style. Confirm
or correct these before submitting. Corrections are saved as the request's old/current
values and appear in Dispatch's copyable summary; Parent DB and cache are not updated.
Existing submitted requests show their saved values read-only.

The current site lookup tries Parent DB first, with an eight-second timeout. SQL errors
or timeout cause cache fallback. The source message distinguishes live current values
from cached current values. Dropdown options come from the tower cache in either case.
A warning about dropdown options alone does not mean the live site lookup failed.
The original lookup snapshot is still checked at submission to detect concurrent
changes; technician corrections are sent separately from that snapshot.

No new database script is required. Deploy both API and client for editable current
values. Check a deliberately incorrect current TOP/sector/IP, submit corrections,
and confirm Dispatch's copied details match. Also check that changing the new TOP
filters its sectors and the selected sector displays its name, not a contract type.
Check Submit/Cancel placement on a small display.

## Editing the window

Edit `SmartGridSuite.Client/Views/Dispatcher/Dialogs/TopChangeWindow.xaml` for all
layout, labels, styles and buttons. `NewRequestPanel` is the technician form;
`ExistingRequestPanel` shows saved requests; `DispatchPanel` contains the copyable
summary and IP-save controls. The companion `TopChangeWindow.xaml.cs` handles loading,
visibility, copying and API calls. Keep named controls/event handlers when restyling.

## Updating from the first TOP Change version

No additional database script is needed if the original setup has already run.
The existing EmailStatus column is retained for compatibility but is no longer used
by the window. Deploy **both API and client**: the old API can still send IP emails.
The updated API removes the email-request endpoint and automatic IP-ready email.

## Setup on the VM

1. Back up the SmartGridSuite application database. Build this branch's API and Windows
   client before replacing either installed version. Do not start the API against a
   production database to run the offline checks below.
2. In your MariaDB SQL tool, select the **SmartGridSuite application database** (the one
   in the API connection string). Run [`sql/top-change-setup.sql`](sql/top-change-setup.sql).
   It adds `ticket_top_changes` and the TOP Change status. Run it **before deploying the
   API** because ticket saves now consult this table. It does not modify Parent DB.
   Existing ticket-status schema updates from this branch must already be installed.
3. No TOP Change email configuration is needed. Any previously configured
   `TopChange:IpRequestRecipients` / `TopChange__IpRequestRecipients` value is unused
   and may be removed. Existing email settings are unchanged.
4. Deploy/restart the API using your existing procedure, then update the Windows client
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
5. In Dispatch, check Copy Request Details for MR, DAC and IG. Paste into a text editor
   and verify all old/new values. Opening or copying the request must not send email.
6. Assign an IP. Check invalid input rejection, the refreshed copyable summary, and the
   new IP in the technician's dashboard/tasks. Confirm no email is sent on save or retry.
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

