#nullable enable
using SmartGridSuite;
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Administration.Trucks;

public sealed class TruckDto
{
    public int Id { get; set; }
    public string TruckNumber { get; set; } = "";

    public int? TruckStyleId { get; set; }
    public string? TruckStyleName { get; set; }

    public bool IsActive { get; set; }

    // compatibility field for older client bits during transition
    public string? DisplayName { get; set; }
}
public sealed class CommitTruckBoardRequest
{
    public DateTime WorkDate { get; set; }

    public List<CommitTruckAssignmentDto> Assignments { get; set; } = new();

    public List<CommitTruckLeadOverrideDto> LeadOverrides { get; set; } = new();
}

public sealed class CommitTruckLeadOverrideDto
{
    public int TruckId { get; set; }

    public int TechnicianId { get; set; }
}

public sealed class CommitTruckAssignmentDto
{
    public int TechnicianId { get; set; }

    public int TruckId { get; set; }
}