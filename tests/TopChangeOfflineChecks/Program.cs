using SmartGridSuite.Contracts.Tickets;

var assignedAt = new DateTime(2026, 1, 10, 12, 0, 0);
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}
var request = new TopChangeDto { State = "PendingIp" };
Check(!TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt), "Early write-up keeps request pending");
request.State = "IpReady";
request.NewIp = "10.2.3.4";
Check(!TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt), "Missing assignment time cannot release protection");
request.IpAssignedAt = assignedAt;
Check(!TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt.AddTicks(-1)), "Write-up started before IP assignment keeps protection");
Check(TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt), "Write-up at assignment time can complete");
Check(TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt.AddMinutes(1)), "Normal later write-up completes request");
Check(TopChangeWorkflow.ActionRequired(request).Contains("10.2.3.4"), "Technician notice includes assigned IP");
request.NewIp = "";
Check(!TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt), "Empty IP cannot release protection");
request.NewIp = "10.2.3.4";
request.State = "Completed";
Check(!TopChangeWorkflow.CanCompleteWithWriteUp(request, assignedAt), "Completed request cannot complete twice");
Check(TopChangeRequestText.TopSector("ACKMWB", "AP1") == "ACKMWB-AP1", "TOP and sector combined");
Check(TopChangeRequestText.TopSector("ACKMWB", "ACKMWB-AP1") == "ACKMWB-AP1", "Already-qualified sector is not duplicated");
Check(TopChangeRequestText.BaseIp("10.80.123.51") == "10.80.123.xxx", "Only final octet replaced");
Check(TopChangeRequestText.BaseIp(null, "10.80.123.52") == "10.80.123.xxx", "IP A fallback when VIP missing");
Check(TopChangeRequestText.BaseIp("10.80.999.1", "10.80.42.2") == "10.80.42.xxx", "Invalid VIP skipped");
Check(TopChangeRequestText.BaseIp("", "10.80.1", "::1") == "", "Missing or invalid IP does not invent a subnet");
var sample = new TopChangeDto { Site = "1234MR", OldTop = "NS_FOB", OldSector = "AP2",
    OldIp = "10.80.244.51", NewTop = "ACKMWB", NewSector = "AP1" };
var expected = "Need to change the TOP site is going to.\n\nSite: 1234MR\n\n" +
    "Current TOP: NS_FOB-AP2\nCurrent IP: 10.80.244.51\n\n" +
    "Requested TOP: ACKMWB-AP1 (Base IP: 10.80.123.xxx)\n\n" +
    "Please provide IP and update tunnels, if applicable.";
Check(TopChangeRequestText.Format(sample, "10.80.123.xxx") == expected, "Copy text matches requested format");
Check(TopChangeRequestText.Format(sample, "").Contains("[unavailable"), "Missing base IP is clearly marked");
sample.NewIp = "10.80.123.77";
Check(TopChangeWorkflow.WriteUpHeader(sample) ==
    "New TOP: ACKMWB-AP1",
    "Completed TOP change write-up header contains only the new TOP");
Check(TopChangeWorkflow.DispatchNote(sample) ==
    "TOP Change — New TOP: ACKMWB-AP1 | New IP: 10.80.123.77",
    "Dispatch note contains new TOP and assigned IP");
Console.WriteLine("TOP change offline checks passed; no API or database was contacted.");
