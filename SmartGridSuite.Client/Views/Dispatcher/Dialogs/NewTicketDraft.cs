using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public sealed class NewTicketDraft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private string _site = "";
    public string Site { get => _site; set => SetField(ref _site, value ?? ""); }

    // Future DB lookup result
    private string _fullSiteName = "";
    public string FullSiteName { get => _fullSiteName; set => SetField(ref _fullSiteName, value ?? ""); }

    private string _problem = "";
    public string Problem { get => _problem; set => SetField(ref _problem, value ?? ""); }

    private string _assignedTo = "";
    public string AssignedTo { get => _assignedTo; set => SetField(ref _assignedTo, value ?? ""); }

    private string _notificationName = "";
    public string NotificationName { get => _notificationName; set => SetField(ref _notificationName, value ?? ""); }

    private string _notificationNumber = "";
    public string NotificationNumber { get => _notificationNumber; set => SetField(ref _notificationNumber, value ?? ""); }

    private string _workOrder = "";
    public string WorkOrder { get => _workOrder; set => SetField(ref _workOrder, value ?? ""); }

    private string _workOrderType = "";
    public string WorkOrderType
    {
        get => _workOrderType;
        set
        {
            if (!SetField(ref _workOrderType, value ?? "")) return;
            OnPropertyChanged(nameof(WorkOrderCodeOptions));

            // If current code no longer valid, clear it
            if (!WorkOrderCodeOptions.Contains(WorkOrderCode))
                WorkOrderCode = "";
        }
    }

    private string _workOrderCode = "";
    public string WorkOrderCode { get => _workOrderCode; set => SetField(ref _workOrderCode, value ?? ""); }

    public IReadOnlyList<string> WorkOrderCodeOptions
    {
        get
        {
            var all = new[] { "HRM2", "HRC1", "HRC2", "HRC3", "HDM1", "HDM2", "HDC1", "HDC2" };

            return WorkOrderType switch
            {
                "Maintenance" => new[] { "HRM2" },
                "Capital" => new[] { "HRC1", "HRC2", "HRC3" },
                "Distribution" => new[] { "HDM1", "HDM2", "HDC1", "HDC2" },
                _ => all
            };
        }
    }

    // Blank default is desired; keep as string for binding
    private string _priorityDays = "";
    public string PriorityDays { get => _priorityDays; set => SetField(ref _priorityDays, value ?? ""); }

    private string _notes = "";
    public string Notes { get => _notes; set => SetField(ref _notes, value ?? ""); }

    private string _createdBy = "";
    public string CreatedBy { get => _createdBy; set => SetField(ref _createdBy, value ?? ""); }
}