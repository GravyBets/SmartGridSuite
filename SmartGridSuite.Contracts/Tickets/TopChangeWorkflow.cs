namespace SmartGridSuite.Contracts.Tickets;

public static class TopChangeWorkflow
{
    public static bool CanCompleteWithWriteUp(TopChangeDto request, DateTime submittedAt) =>
        request.State == "IpReady" && request.IpAssignedAt.HasValue &&
        request.IpAssignedAt.Value <= submittedAt && !string.IsNullOrWhiteSpace(request.NewIp);

    public static string ActionRequired(TopChangeDto request) => request.State == "IpReady"
        ? $"TOP Change — IP ready: {request.NewIp}" : "TOP Change — awaiting IP";
}
