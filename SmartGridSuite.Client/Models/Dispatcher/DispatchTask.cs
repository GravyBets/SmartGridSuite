using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    public class DispatchTask : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private DateTime _occurredAt = DateTime.Now;
        public DateTime OccurredAt
        {
            get => _occurredAt;
            set { _occurredAt = value; OnPropertyChanged(nameof(OccurredAt)); }
        }

        private string _site = "";
        public string Site
        {
            get => _site;
            set { _site = value; OnPropertyChanged(nameof(Site)); }
        }

        private string _tech = "";
        public string Tech
        {
            get => _tech;
            set { _tech = value; OnPropertyChanged(nameof(Tech)); }
        }

        private string _notification = "";
        public string Notification
        {
            get => _notification;
            set { _notification = value; OnPropertyChanged(nameof(Notification)); }
        }

        private string _workOrder = "";
        public string WorkOrder
        {
            get => _workOrder;
            set
            {
                _workOrder = value ?? "";
                OnPropertyChanged(nameof(WorkOrder));
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        private WorkOrderClass _workOrderClass = WorkOrderClass.Maintenance;
        public WorkOrderClass WorkOrderClass
        {
            get => _workOrderClass;
            set
            {
                _workOrderClass = value;
                OnPropertyChanged(nameof(WorkOrderClass));
                OnPropertyChanged(nameof(WorkOrderClassLabel));
            }
        }

        public string WorkOrderClassLabel
            => string.IsNullOrWhiteSpace(WorkOrder)
                ? ""
                : (WorkOrderClass == WorkOrderClass.Capital ? "Cap." : "Maint.");

        private string _actionRequired = "";
        public string ActionRequired
        {
            get => _actionRequired;
            set { _actionRequired = value; OnPropertyChanged(nameof(ActionRequired)); }
        }

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(nameof(Notes)); }
        }

        private string _status = "Open";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        private string _category = "All";
        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(nameof(Category)); }
        }
    }
}
