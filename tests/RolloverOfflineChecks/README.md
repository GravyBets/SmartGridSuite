# Carry-over regression checks

From the repository root with the .NET 8 SDK installed:

```sh
dotnet run --project tests/RolloverOfflineChecks/RolloverOfflineChecks.csproj
```

These nine checks run the production selection helper without a database, API,
credentials, network calls, or third-party test packages. They cover shared tickets,
repeated publications, merging routes, distinct tickets, legacy targets, and empty
input. They do not exercise EF transactions, email delivery, or the daily scheduler.

## Isolated database acceptance tests (never against production)

- Publish one unfinished ticket to two different crews yesterday. Run today's
  rollover after 5 a.m.: both routes must receive it, both source rows must be
  retired, and each destination must reference its corresponding source.
- Republish one of those routes several times before rollover: no duplicate copy.
- Put both previous route owners on one crew today: one current ticket on that
  route, both old source rows retired. Run tomorrow's rollover: neither old source
  may reappear as a second copy.
- Re-run the same day's rollover, including after restarting the test API:
  no duplicate assignments or publications.
- Closed/field-complete tickets and removed source assignments must not carry.
- Today's existing assignments (including drafts and removed rows) must continue
  to suppress automatic carry-over of that ticket; this policy is unchanged.
- Today's published work remains below carried work; unpublished drafts must not
  become published as a side effect.

The 5 a.m. server-local schedule, once-per-process daily completion cache, and
weekend/off-duty behavior are unchanged by this focused multi-crew fix.
