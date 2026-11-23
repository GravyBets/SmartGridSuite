# SmartGridSuite — Dispatch / Field Tech regression QA (3.6.2.4 source)

These changes are on a **draft branch**. They are **not verified by a Windows build,
a live MySQL integration test, or a field rollout**. Test using a nonproduction
API/database and test technicians before updating ClickOnce or the VM.

## 0. Prepare
- [ ] Confirm the deployed 3.6.2.4 source commit against the `feature/tower-defense-arcade-20261026` baseline.
- [ ] Save a VM/database backup; do not edit historical write-up/participant rows manually.
- [ ] Run `dotnet run --project tests/DispatchAssignmentOfflineChecks/DispatchAssignmentOfflineChecks.csproj`.
- [ ] Build the .NET 8 API, Contracts, and Windows WPF Client in Visual Studio.
- [ ] Confirm Light, Dark, and Kuipers themes, laptop scaling, API connectivity, and an offline/reconnect cycle.

## 1. Dispatch → Tasks: per-write-up closeout checklist
- [ ] Submit write-up #1 with Update KMZ and Update SAP Notification.
- [ ] Check only the first Update KMZ. Confirm 1/2 complete, retain saved operator/time.
- [ ] Reopen/reassign ticket and submit write-up #2 with Update KMZ and Update SAP Notification.
- [ ] Confirm two labeled sections, **oldest first**, with submitting tech and timestamp.
- [ ] Confirm #1 Update KMZ remains checked and #2 Update KMZ starts unchecked.
- [ ] Confirm unfinished #1 SAP remains unchecked and visible; no items are hidden.
- [ ] Repeat a third write-up; verify prior group states, ordering, per-group and overall progress.
- [ ] Toggle one item; ensure no checkbox on another submission changes.
- [ ] Close must refuse until every required item on every non-deleted write-up is complete.
- [ ] Optional items are counted in progress but do not block closing.
- [ ] Old write-up narrative and Site History retain original text and participants.

## 2. Current Assigned To versus saved write-up participants
- [ ] Publish Ticket A to Mike/Travis through Daily Assignments. Assigned To shows both.
- [ ] Dispatch opens Edit Ticket and **adds Phillip manually**; Assigned To shows all three.
- [ ] Reassign Travis's truck spot to Brian; Assigned To changes to Mike/Brian/Phillip, not Travis.
- [ ] Remove that crew's Daily Assignment; Assigned To shows **Phillip only**.
- [ ] Assign Ticket A to another crew; show that crew plus Phillip, no stale prior crew.
- [ ] Submit a write-up: preview contains current assignees checked by default and the real submitter always included.
- [ ] **Unassigned ticket regression:** the preview must show only the signed-in submitter, with no `(Unassigned)` checkbox, and the final `CNP Techs:` footer must contain only the actual submitter.
- [ ] Crew with manual additions regression: retain all real assigned technicians (checked by default), and still allow unchecking individual participants.
- [ ] Uncheck Phillip: new submission and Site History credit only checked participants; Phillip remains assigned.
- [ ] Submit again with Phillip checked; new History includes Phillip.
- [ ] Reassign/remove route after submissions; all older write-ups and their saved participant rows remain unchanged in ticket/Site History/each original technician's Field Tech History.
- [ ] Edit a **closed** ticket's Assigned To: no retroactive participants/History records are added to the prior write-up.
- [ ] Test two crews on one ticket, manually assigned technician on top, and route removal of only one crew.
- [ ] Audit pre-existing tickets with a previously polluted `manual_assigned_tech` value before relying on automated cleanup. Do not guess ownership from historical combined text.

## 3. Field Tech → History: 1500 ms responsive debounce
- [ ] Type a query slowly, with >1.5 s between keystrokes. Continue typing while each API request runs.
- [ ] SearchBox never loses keyboard focus, caret position or selection and is never disabled.
- [ ] An old server response cannot replace results for a newer search string.
- [ ] Only one search fires after 1.5 s of no typing; the timer resets for new characters/deletes.
- [ ] Loading indicator stays over result grid, not the search controls.
- [ ] Force a high-latency/failed cellular request; verify typing continues and stale data is not overwritten.
- [ ] Refresh/date controls and offline error presentation still work.

## 4. Dispatch → Tasks: serial copying for selected write-up
- [ ] In Changes, Found/Left equipment SNs appear in their original order with a mini copy icon.
- [ ] Each icon copies **only** that serial and briefly changes to a checkmark.
- [ ] Copy All SNs copies **one value per line**, no labels, bullets, commas, or blank lines.
- [ ] Same serial twice in the selected write-up is copied **twice**, preserving order.
- [ ] Include removed, installed, and Bad From Stock recorded SNs. Missing / (not recorded) yields no button/value.
- [ ] Switch Tasks ticket/write-up; Copy All includes only serials from the currently displayed submission.
- [ ] Preserve other Changes lines (IP/TOP and non-serial equipment notes).
- [ ] Paste bulk copy into Excel: each serial lands on a separate row; verify Light/Dark/Kuipers themes and laptop DPI scaling.

## Release gate
- [ ] Resolve findings on the development branch and re-run the above.
- [ ] Review API and client version numbers/release notes before publishing a **new** release.
- [ ] Explicitly approve merge and deployment; never publish this draft straight into production.
