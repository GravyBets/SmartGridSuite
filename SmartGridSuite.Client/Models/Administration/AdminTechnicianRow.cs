#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace SmartGridSuite.Client.Models.Administration;

//Make this file go away at somepoint

public partial class AdminTechnicianRow : ObservableObject
{
    private string _firstName = "";
    private string _lastName = "";
    private string _title = "";
    private bool _isActive;
    private int? _homeTruckId;
    private string? _homeTruckNumber;
    private string? _homeTruckDisplayName;

    private bool _worksMonday;
    private bool _worksTuesday;
    private bool _worksWednesday;
    private bool _worksThursday;
    private bool _worksFriday;
    private bool _worksSaturday;
    private bool _worksSunday;

    private List<string> _roleCodes = new();

    public int Id { get; init; }
    public string EmployeeId { get; init; } = "";

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value))
                OnPropertyChanged(nameof(FullName));
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetProperty(ref _lastName, value))
                OnPropertyChanged(nameof(FullName));
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? "");
    }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(ActiveEmployeeText));
        }
    }

    public string ActiveEmployeeText => IsActive ? "Yes" : "No";

    public int? HomeTruckId
    {
        get => _homeTruckId;
        set => SetProperty(ref _homeTruckId, value);
    }

    public string? HomeTruckNumber
    {
        get => _homeTruckNumber;
        set
        {
            if (SetProperty(ref _homeTruckNumber, value))
                OnPropertyChanged(nameof(HomeTruckSummary));
        }
    }

    public string? HomeTruckDisplayName
    {
        get => _homeTruckDisplayName;
        set
        {
            if (SetProperty(ref _homeTruckDisplayName, value))
                OnPropertyChanged(nameof(HomeTruckSummary));
        }
    }

    public string HomeTruckSummary
        => !string.IsNullOrWhiteSpace(HomeTruckDisplayName)
            ? HomeTruckDisplayName!
            : (HomeTruckNumber ?? "");

    public bool WorksMonday
    {
        get => _worksMonday;
        set
        {
            if (SetProperty(ref _worksMonday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool WorksTuesday
    {
        get => _worksTuesday;
        set
        {
            if (SetProperty(ref _worksTuesday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool WorksWednesday
    {
        get => _worksWednesday;
        set
        {
            if (SetProperty(ref _worksWednesday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool WorksThursday
    {
        get => _worksThursday;
        set
        {
            if (SetProperty(ref _worksThursday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool WorksFriday
    {
        get => _worksFriday;
        set
        {
            if (SetProperty(ref _worksFriday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool WorksSaturday
    {
        get => _worksSaturday;
        set
        {
            if (SetProperty(ref _worksSaturday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public bool WorksSunday
    {
        get => _worksSunday;
        set
        {
            if (SetProperty(ref _worksSunday, value))
                OnPropertyChanged(nameof(ScheduleSummary));
        }
    }

    public List<string> RoleCodes
    {
        get => _roleCodes;
        set
        {
            if (SetProperty(ref _roleCodes, value ?? new()))
                OnPropertyChanged(nameof(RolesSummary));
        }
    }

    public string RolesSummary
        => RoleCodes.Count == 0
            ? ""
            : string.Join(", ", RoleCodes.Select(ToDisplayRole));

    public string ScheduleSummary
    {
        get
        {
            var days = new List<string>();

            if (WorksMonday) days.Add("Mon");
            if (WorksTuesday) days.Add("Tue");
            if (WorksWednesday) days.Add("Wed");
            if (WorksThursday) days.Add("Thu");
            if (WorksFriday) days.Add("Fri");
            if (WorksSaturday) days.Add("Sat");
            if (WorksSunday) days.Add("Sun");

            return days.Count == 0 ? "None" : string.Join(", ", days);
        }
    }

    private static string ToDisplayRole(string code)
        => (code ?? "").Trim().ToUpperInvariant() switch
        {
            "TECHNICIAN" => "Technician",
            "DISPATCH" => "Dispatch",
            "ADMIN" => "Admin",
            _ => code ?? ""
        };
}