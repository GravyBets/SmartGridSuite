using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    public sealed class DispatchCloseoutChecklistItem :
        INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? name = null)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(name));
            }
        }

        public long Id { get; set; }

        public long SubmissionId { get; set; }

        public uint? DefinitionId { get; set; }

        public string DisplayName { get; set; } = "";

        public int SortOrder { get; set; }

        public bool IsRequired { get; set; }

        public string ConditionType { get; set; } = "";

        public uint? WriteUpFlagId { get; set; }

        public uint? ReferToOptionId { get; set; }

        private bool _isCompleted;

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted == value)
                    return;

                _isCompleted = value;
                OnPropertyChanged();
            }
        }

        private string _completedBy = "";

        public string CompletedBy
        {
            get => _completedBy;
            set
            {
                var clean = value?.Trim() ?? "";

                if (_completedBy == clean)
                    return;

                _completedBy = clean;
                OnPropertyChanged();
            }
        }

        private DateTime? _completedAt;

        public DateTime? CompletedAt
        {
            get => _completedAt;
            set
            {
                if (_completedAt == value)
                    return;

                _completedAt = value;
                OnPropertyChanged();
            }
        }

        public string RequiredLabel =>
            IsRequired
                ? "Required"
                : "Optional";
    }
}