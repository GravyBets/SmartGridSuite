using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    public class DispatchTask : INotifyPropertyChanged
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

        private long _ticketId;
        public long TicketId
        {
            get => _ticketId;
            set
            {
                if (_ticketId == value) return;

                _ticketId = value;
                OnPropertyChanged();
            }
        }

        private DateTime _occurredAt = DateTime.Now;
        public DateTime OccurredAt
        {
            get => _occurredAt;
            set
            {
                if (_occurredAt == value) return;

                _occurredAt = value;
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

        private string _tech = "";
        public string Tech
        {
            get => _tech;
            set
            {
                var cleaned = Clean(value);
                if (_tech == cleaned) return;

                _tech = cleaned;
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

        private string _workOrder = "";
        public string WorkOrder
        {
            get => _workOrder;
            set
            {
                var cleaned = Clean(value);
                if (_workOrder == cleaned) return;

                _workOrder = cleaned;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        private WorkOrderClass _workOrderClass = WorkOrderClass.Unknown;
        public WorkOrderClass WorkOrderClass
        {
            get => _workOrderClass;
            set
            {
                if (_workOrderClass == value) return;

                _workOrderClass = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        public string WorkOrderClassLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(WorkOrder))
                    return "";

                return WorkOrderClass switch
                {
                    WorkOrderClass.Capital => "Capital",
                    WorkOrderClass.Maintenance => "Maintenance",
                    WorkOrderClass.Distribution => "Distribution",
                    _ => ""
                };
            }
        }

        private string _actionRequired = "";
        public string ActionRequired
        {
            get => _actionRequired;
            set
            {
                var cleaned = Clean(value);
                if (_actionRequired == cleaned) return;

                _actionRequired = cleaned;
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

        private string _category = "";
        public string Category
        {
            get => _category;
            set
            {
                var cleaned = Clean(value);
                if (_category == cleaned) return;

                _category = cleaned;
                OnPropertyChanged();
            }
        }
    }
}