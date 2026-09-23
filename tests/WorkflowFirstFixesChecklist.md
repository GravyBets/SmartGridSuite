# Network Test and Work Order Type checks

These changes require a client rebuild and an API update for Work Order Type.
No schema change is required. Run against an isolated test API/database.

- MR, DAC and IG: run pings, then press Test. IP colors should update while
  ping summaries remain intact. Repeat for successful, failed and blank IPs.
- Submit preview with ping stats included: no Testing/Test Successful/Test Failed
  text should replace the ping statistics. Test both stopped and running pings.
- Create a ticket with no WO number and each supported WO Type, then reopen it:
  the type should persist.
- Edit only WO Type on a ticket without a number: Save should enable and persist it.
- Remove an existing WO number: the selected type should remain.
- Bulk-set a type across tickets with and without WO numbers: all existing selected
  tickets should update. Blank type should clear it in both cases.
- Existing WO number length validation and Work Order Code rules should still apply.

Source/diff reviewed; .NET SDK is unavailable in the editing environment, so
Windows build and live behavior remain to be verified.
