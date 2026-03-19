using System;
using System.ComponentModel;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    public class DispatchTicket : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _site = "";
        public string Site { get => _site; set { _site = value ?? ""; OnPropertyChanged(nameof(Site)); } }

        private string _notification = "";
        public string Notification { get => _notification; set { _notification = value ?? ""; OnPropertyChanged(nameof(Notification)); } }

        private string _status = "Open";
        public string Status { get => _status; set { _status = value ?? ""; OnPropertyChanged(nameof(Status)); } }

        private string _assignedTech = "(Unassigned)";
        public string AssignedTech { get => _assignedTech; set { _assignedTech = value ?? ""; OnPropertyChanged(nameof(AssignedTech)); } }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt { get => _createdAt; set { _createdAt = value; OnPropertyChanged(nameof(CreatedAt)); } }

        private DateTime _lastActivityAt = DateTime.Now;
        public DateTime LastActivityAt { get => _lastActivityAt; set { _lastActivityAt = value; OnPropertyChanged(nameof(LastActivityAt)); } }

        private string _currentWorkOrder = "";

        private long _id;
        public long Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }

        private string _notificationName = "";
        public string NotificationName { get => _notificationName; set { _notificationName = value ?? ""; OnPropertyChanged(nameof(NotificationName)); } }

        private string _groupCode = "";
        public string GroupCode { get => _groupCode; set { _groupCode = value ?? ""; OnPropertyChanged(nameof(GroupCode)); } }

        private string _workOrderType = "";
        public string WorkOrderType
        {
            get => _workOrderType;
            set
            {
                _workOrderType = value ?? "";
                OnPropertyChanged(nameof(WorkOrderType));
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        private int _priorityDays = 5;
        public int PriorityDays { get => _priorityDays; set { _priorityDays = value; OnPropertyChanged(nameof(PriorityDays)); OnPropertyChanged(nameof(PriorityLabel)); } }

        public string PriorityLabel => $"{PriorityDays}d";

        private string _problem = "";
        public string Problem { get => _problem; set { _problem = value ?? ""; OnPropertyChanged(nameof(Problem)); } }

        private string _notes = "";
        public string Notes { get => _notes; set { _notes = value ?? ""; OnPropertyChanged(nameof(Notes)); } }

        private string _createdBy = "";
        public string CreatedBy { get => _createdBy; set { _createdBy = value ?? ""; OnPropertyChanged(nameof(CreatedBy)); } }

        private ulong? _taskCategoryId;
        public ulong? TaskCategoryId
        {
            get => _taskCategoryId;
            set { _taskCategoryId = value; OnPropertyChanged(nameof(TaskCategoryId)); }
        }

        private string _taskCategoryName = "";
        public string TaskCategoryName
        {
            get => _taskCategoryName;
            set { _taskCategoryName = value ?? ""; OnPropertyChanged(nameof(TaskCategoryName)); }
        }

        private string _actionRequiredOverride = "";
        public string ActionRequiredOverride
        {
            get => _actionRequiredOverride;
            set { _actionRequiredOverride = value ?? ""; OnPropertyChanged(nameof(ActionRequiredOverride)); }
        }
        public string CurrentWorkOrder
        {
            get => _currentWorkOrder;
            set
            {
                _currentWorkOrder = value ?? "";
                OnPropertyChanged(nameof(CurrentWorkOrder));
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        private WorkOrderClass _woClass = WorkOrderClass.Maintenance;
        public WorkOrderClass WoClass
        {
            get => _woClass;
            set
            {
                _woClass = value;
                OnPropertyChanged(nameof(WoClass));
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        public string WorkOrderClassLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CurrentWorkOrder))
                    return "";

                if (WorkOrderType.Equals("Capital", StringComparison.OrdinalIgnoreCase) ||
                    WorkOrderType.Equals("Cap", StringComparison.OrdinalIgnoreCase))
                    return "Cap.";

                if (WorkOrderType.Equals("Distribution", StringComparison.OrdinalIgnoreCase) ||
                    WorkOrderType.Equals("Dist", StringComparison.OrdinalIgnoreCase))
                    return "Dist.";

                return "Maint.";
            }
        }

        private string _summary = "";
        public string Summary { get => _summary; set { _summary = value ?? ""; OnPropertyChanged(nameof(Summary)); } }
    }
}