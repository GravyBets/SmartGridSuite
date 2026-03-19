#nullable enable
using SmartGridSuite;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace SmartGridSuite.Contracts.Administration.Technicians;

public sealed class CreateTechnicianRequest
{
    public string EmployeeId { get; set; } = "";

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Title { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public int? HomeTruckId { get; set; }

    public bool WorksMonday { get; set; } = true;
    public bool WorksTuesday { get; set; } = true;
    public bool WorksWednesday { get; set; } = true;
    public bool WorksThursday { get; set; } = true;
    public bool WorksFriday { get; set; } = true;
    public bool WorksSaturday { get; set; } = false;
    public bool WorksSunday { get; set; } = false;

    public List<string> RoleCodes { get; set; } = new();
}