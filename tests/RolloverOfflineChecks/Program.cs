using SmartGridSuite.Api.Services;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS: {name}");
    checks++;
}

Snapshot[] Latest(params Snapshot[] rows) =>
    DailyAssignmentRolloverSelection.LatestPerAssignment(rows, x => x.SourceId).ToArray();
Snapshot[] Destinations(params Snapshot[] rows) =>
    DailyAssignmentRolloverSelection.OnePerDestination(rows, x => x.TicketId, x => x.Target).ToArray();

var crewA = new Snapshot(101, 42, "Technician:1", 3);
var crewB = new Snapshot(102, 42, "Technician:2", 2);
var olderA = crewA with { Version = 1 };

Check(Latest(crewA, crewB).Length == 2,
    "Same ticket retains both independent crew assignments");
Check(Latest(crewA, crewB, olderA).SequenceEqual(new[] { crewA, crewB }),
    "Repeated publications retain only the newest snapshot per assignment");
Check(Latest(crewA with { SourceId = null }).Length == 0,
    "Snapshots without a source lifecycle are ignored");
Check(Destinations(Latest(crewA, crewB, olderA)).Length == 2,
    "Separate current-day crews each receive the shared ticket");

var combinedB = crewB with { Target = crewA.Target };
var sourcesToAdvance = Latest(crewA, combinedB, olderA);
var combined = Destinations(sourcesToAdvance);
Check(sourcesToAdvance.Length == 2 && combined.Length == 1 && combined[0] == crewA,
    "Merged routes keep both source lifecycles to advance but only one destination copy");
Check(Destinations(crewA, crewA with { TicketId = 43 }).Length == 2,
    "Different tickets on the same route remain distinct");
Check(Destinations(crewA, crewB with { Target = "Truck:1" }).Length == 2,
    "Legacy truck and technician route identities remain distinct");
Check(Latest().Length == 0 && Destinations().Length == 0,
    "Empty input creates no assignments");
Check(Destinations(combined).SequenceEqual(combined),
    "Destination selection is idempotent");
Console.WriteLine($"{checks} rollover selection checks passed.");

sealed record Snapshot(ulong? SourceId, long TicketId, string Target, int Version);
