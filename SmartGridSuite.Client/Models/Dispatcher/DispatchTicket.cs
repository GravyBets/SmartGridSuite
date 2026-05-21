using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    public class DispatchTicket : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            if (!string.IsNullOrWhiteSpace(name))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static string Clean(string? value)
        {
            return value?.Trim() ?? "";
        }

        private static string CleanAssignedTech(string? value)
        {
            var cleaned = Clean(value);
            return string.IsNullOrWhiteSpace(cleaned) ? "(Unassigned)" : cleaned;
        }

        private static string NormalizeWorkOrderClassLabel(string? workOrderType)
        {
            var value = Clean(workOrderType);

            if (value.Equals("Capital", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Cap", StringComparison.OrdinalIgnoreCase))
            {
                return "Cap.";
            }

            if (value.Equals("Distribution", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Dist", StringComparison.OrdinalIgnoreCase))
            {
                return "Dist.";
            }

            if (value.Equals("Maintenance", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Maint", StringComparison.OrdinalIgnoreCase))
            {
                return "Maint.";
            }

            return "";
        }

        private long _id;
        public long Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged();
            }
        }

        private string _site = "";
        public string Site
        {
            get => _site;
            set
            {
                var cleaned = Clean(value);
                if (_site == cleaned) return;

                _site = cleaned;
                OnPropertyChanged();
            }
        }

        private string _notificationName = "";
        public string NotificationName
        {
            get => _notificationName;
            set
            {
                var cleaned = Clean(value);
                if (_notificationName == cleaned) return;

                _notificationName = cleaned;
                OnPropertyChanged();
            }
        }

        private string _notification = "";
        public string Notification
        {
            get => _notification;
            set
            {
                var cleaned = Clean(value);
                if (_notification == cleaned) return;

                _notification = cleaned;
                OnPropertyChanged();
            }
        }

        private string _status = "Open";
        public string Status
        {
            get => _status;
            set
            {
                var cleaned = Clean(value);
                if (_status == cleaned) return;

                _status = cleaned;
                OnPropertyChanged();
            }
        }

        private string _assignedTech = "(Unassigned)";
        public string AssignedTech
        {
            get => _assignedTech;
            set
            {
                var cleaned = CleanAssignedTech(value);
                if (_assignedTech == cleaned) return;

                _assignedTech = cleaned;
                OnPropertyChanged();
            }
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set
            {
                if (_createdAt == value) return;

                _createdAt = value;
                OnPropertyChanged();
            }
        }

        private DateTime _lastActivityAt = DateTime.Now;
        public DateTime LastActivityAt
        {
            get => _lastActivityAt;
            set
            {
                if (_lastActivityAt == value) return;

                _lastActivityAt = value;
                OnPropertyChanged();
            }
        }

        private string _currentWorkOrder = "";
        public string CurrentWorkOrder
        {
            get => _currentWorkOrder;
            set
            {
                var cleaned = Clean(value);
                if (_currentWorkOrder == cleaned) return;

                _currentWorkOrder = cleaned;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        private string _workOrderType = "";
        public string WorkOrderType
        {
            get => _workOrderType;
            set
            {
                var cleaned = Clean(value);
                if (_workOrderType == cleaned) return;

                _workOrderType = cleaned;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        private string _groupCode = "";
        public string GroupCode
        {
            get => _groupCode;
            set
            {
                var cleaned = Clean(value);
                if (_groupCode == cleaned) return;

                _groupCode = cleaned;
                OnPropertyChanged();
            }
        }

        private int _priorityDays = 5;
        public int PriorityDays
        {
            get => _priorityDays;
            set
            {
                if (_priorityDays == value) return;

                _priorityDays = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriorityLabel));
            }
        }

        public string PriorityLabel => $"{PriorityDays}d";

        private string _problem = "";
        public string Problem
        {
            get => _problem;
            set
            {
                var cleaned = Clean(value);
                if (_problem == cleaned) return;

                _problem = cleaned;
                OnPropertyChanged();
            }
        }

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set
            {
                var cleaned = value ?? "";
                if (_notes == cleaned) return;

                _notes = cleaned;
                OnPropertyChanged();
            }
        }

        private string _dispatchNotes = "";
        public string DispatchNotes
        {
            get => _dispatchNotes;
            set
            {
                var cleaned = value ?? "";
                if (_dispatchNotes == cleaned) return;

                _dispatchNotes = cleaned;
                OnPropertyChanged();
            }
        }

        private string _createdBy = "";
        public string CreatedBy
        {
            get => _createdBy;
            set
            {
                var cleaned = Clean(value);
                if (_createdBy == cleaned) return;

                _createdBy = cleaned;
                OnPropertyChanged();
            }
        }

        private ulong? _taskCategoryId;
        public ulong? TaskCategoryId
        {
            get => _taskCategoryId;
            set
            {
                if (_taskCategoryId == value) return;

                _taskCategoryId = value;
                OnPropertyChanged();
            }
        }

        private string _taskCategoryName = "";
        public string TaskCategoryName
        {
            get => _taskCategoryName;
            set
            {
                var cleaned = Clean(value);
                if (_taskCategoryName == cleaned) return;

                _taskCategoryName = cleaned;
                OnPropertyChanged();
            }
        }

        private string _actionRequiredOverride = "";
        public string ActionRequiredOverride
        {
            get => _actionRequiredOverride;
            set
            {
                var cleaned = Clean(value);
                if (_actionRequiredOverride == cleaned) return;

                _actionRequiredOverride = cleaned;
                OnPropertyChanged();
            }
        }

        private WorkOrderClass _woClass = WorkOrderClass.Unknown;
        public WorkOrderClass WoClass
        {
            get => _woClass;
            set
            {
                if (_woClass == value) return;

                _woClass = value;
                OnPropertyChanged();
            }
        }

        public string WorkOrderClassLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CurrentWorkOrder))
                    return "";

                return NormalizeWorkOrderClassLabel(WorkOrderType);
            }
        }

        private string _summary = "";
        public string Summary
        {
            get => _summary;
            set
            {
                var cleaned = Clean(value);
                if (_summary == cleaned) return;

                _summary = cleaned;
                OnPropertyChanged();
            }
        }
    }
}