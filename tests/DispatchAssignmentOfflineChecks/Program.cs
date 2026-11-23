using SmartGridSuite.Api.Services;

// Pure offline checks: no database, VM, API, VPN, or WPF required.
var checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
    checks++;
}

var manual = TicketAssigneeText.ApplyManualDelta(
    "(Unassigned)", "Mike & Travis", "Mike, Travis & Phillip");

Check(manual == "Phillip",
    "Adding Phillip through Edit Ticket does not convert Mike/Travis's crew membership to permanent manual assignments");
Check(TicketAssigneeText.Merge(manual, null) == "Phillip",
    "Phillip remains assigned after Mike/Travis's Daily Assignment is removed");

var withCrew = TicketAssigneeText.Merge(manual, "Mike & Brian");
Check(withCrew.Contains("Mike") && withCrew.Contains("Brian") &&
      withCrew.Contains("Phillip") && !withCrew.Contains("Travis"),
    "A new crew adds current members without retaining a removed technician");

Check(TicketAssigneeText.ApplyManualDelta(
    "Phillip", "Mike & Phillip", "Mike") == "(Unassigned)",
    "Explicitly removing a manual assignee removes only the manual source");

Check(TicketAssigneeText.ApplyManualDelta(
    "Phillip & Alice", "Mike, Phillip & Alice", "Mike & Alice") == "Alice",
    "Removing one manually assigned person retains the other manual person");

Check(TicketAssigneeText.ApplyManualDelta(
    null, "Legacy Mike", "Legacy Mike & Phillip") == "Legacy Mike & Phillip",
    "Unclassified legacy assignments are preserved rather than silently discarded");

Check(TicketAssigneeText.ApplyManualDelta(
    "Phillip", "Mike & Phillip", "Mike & Phillip") == "Phillip",
    "An unrelated ticket edit does not change manual assignment provenance");

Console.WriteLine($"{checks} offline assignment checks passed.");
