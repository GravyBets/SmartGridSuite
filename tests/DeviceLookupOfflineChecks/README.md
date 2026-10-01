Run from the repository root:

```powershell
dotnet run --project tests/DeviceLookupOfflineChecks/DeviceLookupOfflineChecks.csproj
```

Uses an in-memory SmartGridSuite database. No company database is contacted. Covers exact site/notification/WO routing, opt-in text searching, related records, deleted history, deduplication, cancellation and partial results, native SQL parameter conversion, input validation, theme styles, and both navigation layouts.

Live verification after updating both API and client:

1. In Swagger, search `/api/device-lookup?query=336546122&searchType=DeviceSerialNumber`, using a known current device SN. Confirm matching equipment and related records, and compare elapsed time with the original search.
2. Test a PMR SIM in each slot with `searchType=Sim`. Confirm full PMR fields remain visible, including records without a current AMS association.
3. Test `searchType=Site` and `searchType=IpAddress` with known values. Confirm only the selected identifier fields match, including PMR's own site association where present.
4. Test a notification and WO. Confirm matching tickets and their site's history/notes appear. Try `searchType=HistoryText` to find an identifier that appears only in past write-ups.
5. Compare light and dark themes while changing result tabs. Check the tab strip, empty area, populated rows, headers, selection, and dropdown. Device Lookup must be last in expanded and collapsed navigation, and startup must still open Tasks.
6. Navigate away during a search, then return and search again. Controls must recover. A slow or unavailable database must show a warning alongside any records already read.

Identifier searches use exact matches. HistoryText is the explicit broader search. The Parent DB phase has a 6-second cancellation budget and the overall database search has a 12-second budget, below the client's existing 15-second HTTP timeout. Cancellation depends on the database provider; live timing and Windows appearance still require the checks above. Results are limited to 50 equipment rows per category and the latest 250 tickets/history/notes per category, with warnings at those limits.
