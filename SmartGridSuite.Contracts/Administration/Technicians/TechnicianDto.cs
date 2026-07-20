#nullable enable
using SmartGridSuite;
using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Administration.Technicians;

public sealed class TechnicianDto
{
    public int Id { get; set; }
    public string EmployeeId { get; set; } = "";

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string? EmailAddress { get; set; }
    public string ScheduleText { get; set; } = "";

    public bool IsActive { get; set; }

    public int? HomeTruckId { get; set; }
    public string? HomeTruckNumber { get; set; }
    public string? HomeTruckDisplayName { get; set; }

    public bool WorksMonday { get; set; }
    public bool WorksTuesday { get; set; }
    public bool WorksWednesday { get; set; }
    public bool WorksThursday { get; set; }
    public bool WorksFriday { get; set; }
    public bool WorksSaturday { get; set; }
    public bool WorksSunday { get; set; }

    public List<string> RoleCodes { get; set; } = new();

    // Compatibility fields for existing screens while we finish the refactor
    public bool IsOnShift { get; set; }
    public string? TruckNumber { get; set; }
}