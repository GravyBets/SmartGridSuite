namespace SmartGridSuite.Contracts.Tickets;

public sealed class TopChangeContextDto
{
    public long TicketId { get; set; }
    public string Site { get; set; } = "";
    public string SiteKind { get; set; } = "";
    public string CurrentTop { get; set; } = "";
    public string CurrentSector { get; set; } = "";
    public string CurrentIp { get; set; } = "";
    public string DataWarning { get; set; } = "";
    public List<TopChangeSectorOption> Sectors { get; set; } = new();
    public TopChangeDto? Request { get; set; }
}

public sealed class TopChangeSectorOption
{
    public int TopId { get; set; }
    public int SectorId { get; set; }
    public string Top { get; set; } = "";
    public string Sector { get; set; } = "";
}

public class TopChangeDto
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string Site { get; set; } = "";
    public string SiteKind { get; set; } = "";
    public string OldTop { get; set; } = "";
    public string OldSector { get; set; } = "";
    public string OldIp { get; set; } = "";
    public int NewTopId { get; set; }
    public int NewSectorId { get; set; }
    public string NewTop { get; set; } = "";
    public string NewSector { get; set; } = "";
    public string NewIp { get; set; } = "";
    public bool AlreadyChanged { get; set; }
    public string State { get; set; } = "PendingIp";
    public string RequestedBy { get; set; } = "";
    public string IpAssignedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public DateTime? IpAssignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string EmailStatus { get; set; } = "";
}

public sealed class CreateTopChangeRequest
{
    public Guid ClientRequestId { get; set; }
    public int NewSectorId { get; set; }
    public bool AlreadyChanged { get; set; }
    public string RequestedBy { get; set; } = "";
    // Compare the snapshot the technician reviewed to current server data.
    public string ExpectedTop { get; set; } = "";
    public string ExpectedSector { get; set; } = "";
    public string ExpectedIp { get; set; } = "";
}

public sealed class AssignTopChangeIpRequest
{
    public string Ip { get; set; } = "";
    public string AssignedBy { get; set; } = "";
}

public sealed class TopChangeEmailResult
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}
