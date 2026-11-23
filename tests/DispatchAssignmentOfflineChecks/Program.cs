using SmartGridSuite.Api.Services;
using SmartGridSuite.Client.Models.Dispatcher;

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

var changes = DispatchEquipmentSerialExtractor.Extract(
    "Found Enclosure SN: 309831\\n" +
    "Left Enclosure SN: 4509604227-232\\n" +
    "Left Enclosure SN: 4509604227-232\\n" +
    "Bad From Stock P60 SN: (not recorded)\\n" +
    "Equipment replacement: enclosure");

Check(changes.Count == 5,
    "All equipment replacement lines, including duplicates and non-serial rows, remain displayed");
Check(changes.Count(x => x.HasSerialNumber) == 3,
    "Unrecorded/absent serial values are not treated as copyable");
Check(changes[0].SerialNumber == "309831" && changes[1].SerialNumber == "4509604227-232",
    "Found/Left serials retain their full values and order");
Check(DispatchEquipmentSerialExtractor.CopyAll(changes) ==
      string.Join(Environment.NewLine, "309831", "4509604227-232", "4509604227-232"),
    "Copy All preserves duplicate serials in a vertical stack without labels");
Check(DispatchEquipmentSerialExtractor.CopyAll(
    DispatchEquipmentSerialExtractor.Extract("Equipment replacement: P60")) == "",
    "No serials means empty clipboard content");

Console.WriteLine($"{checks} offline assignment and serial copy checks passed.");
