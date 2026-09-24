namespace SmartGridSuite.Contracts.Tickets;

public static class TopChangeWorkflow
{
    public static bool CanCompleteWithWriteUp(TopChangeDto request, DateTime submittedAt) =>
        request.State == "IpReady" && request.IpAssignedAt.HasValue &&
        request.IpAssignedAt.Value <= submittedAt && !string.IsNullOrWhiteSpace(request.NewIp);

    public static string ActionRequired(TopChangeDto request) => request.State == "IpReady"
        ? $"TOP Change — IP ready: {request.NewIp}" : "TOP Change — awaiting IP";

    public static string Destination(TopChangeDto request) =>
        TopChangeRequestText.TopSector(request.NewTop, request.NewSector);

    public static string WriteUpLine(TopChangeDto request) =>
        $"New TOP: {Destination(request)}";

    public static string WriteUpHeader(TopChangeDto request) =>
        WriteUpLine(request);

    public static string DispatchNote(TopChangeDto request) =>
        $"TOP Change — New TOP: {Destination(request)} | New IP: {request.NewIp}";
}
