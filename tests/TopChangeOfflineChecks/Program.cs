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
Console.WriteLine("TOP change offline checks passed; no API or database was contacted.");
