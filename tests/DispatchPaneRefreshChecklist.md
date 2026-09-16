# Dispatch pane refresh acceptance checks

This is a WPF/client-only change. No API deployment or database migration is required.
Build the client on Windows with the .NET 8 SDK. These checks require a reachable
test API; source inspection alone does not verify WPF lifecycle behavior.

1. Open Tasks, Tickets, Daily Assignments, and Technicians / Truck Assignments.
   Each should show its loading overlay and fetch current data on first entry.
2. Leave each pane, change its data from another client, then return. The update
   must appear without pressing Refresh. Repeat with expanded and collapsed navigation.
3. Tasks: leave a row expanded, switch away, and return within five seconds.
   The pane must still refresh both status options and task data. Search/status
   selections should remain when valid. Idle refresh should still avoid interrupting
   someone reading an expanded task.
4. Tasks: switch away and back while loading over a slow connection. A canceled
   request must not prevent the follow-up refresh or leave the overlay stuck.
5. Tickets: set search, status, technician and date filters, then switch away/back.
   Preserve valid filter selections, refresh technician/status choices, ticket rows
   and summary. Like manual Refresh, restart paging at the first page.
6. Daily Assignments and Truck Assignments: select a future work date, switch away
   and return. Refresh that same date, not today. Preserve Daily Assignments pool
   search/filter settings; do not publish pending server-side assignments.
7. Truck Assignments: edit the board, then navigate away. Existing Commit / Discard /
   Cancel prompt must still work. Cancel keeps edits and the current pane. Returning
   after Commit or Discard reloads the board. Reloading a dirty pane must not silently
   discard edits; it uses the manual Refresh confirmation.
8. Verify all four manual Refresh buttons still work. Simulate API failure and
   retry by leaving and returning; loading controls should recover.

Scope: refresh on first entry and navigation back to a cached pane. Clicking an
already-selected navigation item without leaving it is not a pane load; use Refresh
for that case. No release deployment is performed by these changes.
