using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SmartGridSuite.Contracts.SiteNotes;

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

        private string _dispatchRequestDetails = "";

        public string DispatchRequestDetails
        {
            get => _dispatchRequestDetails;

            set
            {
                var cleaned =
                    value?.Trim() ?? "";

                if (_dispatchRequestDetails == cleaned)
                    return;

                _dispatchRequestDetails =
                    cleaned;

                OnPropertyChanged();
            }
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

        private bool _siteNotesLoaded;

        public bool SiteNotesLoaded
        {
            get => _siteNotesLoaded;
            set
            {
                if (_siteNotesLoaded == value)
                    return;

                _siteNotesLoaded = value;
                OnPropertyChanged();
            }
        }

        private bool _isSiteNotesLoading;

        public bool IsSiteNotesLoading
        {
            get => _isSiteNotesLoading;
            set
            {
                if (_isSiteNotesLoading == value)
                    return;

                _isSiteNotesLoading = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<SiteNoteDto> SiteNotes { get; } = new();

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
                OnPropertyChanged(nameof(IsClosed));
                OnPropertyChanged(nameof(CanCloseFromTasks));
            }
        }

        public bool IsClosed =>
            Status.Equals(
                "Closed",
                StringComparison.OrdinalIgnoreCase) ||
            Status.Equals(
                "Completed",
                StringComparison.OrdinalIgnoreCase) ||
            Status.Equals(
                "Cancelled",
                StringComparison.OrdinalIgnoreCase) ||
            Status.Equals(
                "Canceled",
                StringComparison.OrdinalIgnoreCase);

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

        private long? _submissionId;

        public long? SubmissionId
        {
            get => _submissionId;
            set
            {
                if (_submissionId == value)
                    return;

                _submissionId = value;
                OnPropertyChanged();
            }
        }

        private DateTime? _submittedAt;

        public DateTime? SubmittedAt
        {
            get => _submittedAt;
            set
            {
                if (_submittedAt == value)
                    return;

                _submittedAt = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SubmittedWriteUpCopyText));
            }
        }

        private string _submittedByName = "";

        public string SubmittedByName
        {
            get => _submittedByName;
            set
            {
                var cleaned = Clean(value);

                if (_submittedByName == cleaned)
                    return;

                _submittedByName = cleaned;
                OnPropertyChanged();
            }
        }

        private string _submittedWriteUp = "";

        public string SubmittedWriteUp
        {
            get => _submittedWriteUp;
            set
            {
                var cleaned = value ?? "";

                if (_submittedWriteUp == cleaned)
                    return;

                _submittedWriteUp = cleaned;

                OnPropertyChanged();
                OnPropertyChanged(nameof(SubmittedWriteUpDisplay));
                OnPropertyChanged(nameof(SubmittedWriteUpCopyText));
                OnPropertyChanged(nameof(IpChangeLinesDisplay));
                OnPropertyChanged(nameof(EquipmentReplacementLinesDisplay));
                OnPropertyChanged(nameof(DispatchChangesDisplay));
            }
        }

        public string SubmittedWriteUpDisplay => BuildDispatchWriteUpDisplay(SubmittedWriteUp);

        public string IpChangeLinesDisplay => BuildIpChangeLinesDisplay(SubmittedWriteUpDisplay);

        private static string BuildIpChangeLinesDisplay(
            string? writeUpText)
        {
            var text =
                writeUpText?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(text))
                return "";

            var lines =
                text.Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.None)
                    .Select(x => x.Trim())
                    .Where(x =>
                        x.StartsWith(
                            "New ",
                            StringComparison.OrdinalIgnoreCase) &&
                        x.Contains(
                            " IP:",
                            StringComparison.OrdinalIgnoreCase))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            return lines.Count == 0
                ? ""
                : string.Join(
                    Environment.NewLine,
                    lines);
        }

        public string EquipmentReplacementLinesDisplay => BuildEquipmentReplacementLinesDisplay(SubmittedWriteUpDisplay);

        public string DispatchChangesDisplay
        {
            get
            {
                var sections =
                    new[]
                    {
                IpChangeLinesDisplay,
                EquipmentReplacementLinesDisplay
                    }
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x))
                    .ToList();

                return sections.Count == 0
                    ? ""
                    : string.Join(
                        Environment.NewLine,
                        sections);
            }
        }

        private static string BuildEquipmentReplacementLinesDisplay(
            string? writeUpText)
        {
            var text =
                writeUpText?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(text))
                return "";

            const string equipmentHeader =
                "----Equipment Replacements----";

            var lines =
                text.Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.None)
                    .ToList();

            var headerIndex =
                lines.FindIndex(
                    x =>
                        x.Trim().Equals(
                            equipmentHeader,
                            StringComparison.OrdinalIgnoreCase));

            if (headerIndex < 0)
                return "";

            var equipmentLines =
                new List<string>();

            for (var i = headerIndex + 1;
                 i < lines.Count;
                 i++)
            {
                var line =
                    lines[i].Trim();

                /*
                 * Reached the next formatted write-up section.
                 */
                if (line.StartsWith(
                        "----",
                        StringComparison.Ordinal))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                equipmentLines.Add(line);
            }

            return equipmentLines.Count == 0
                ? ""
                : string.Join(
                    Environment.NewLine,
                    equipmentLines.Distinct(
                        StringComparer.OrdinalIgnoreCase));
        }

        public string SubmittedWriteUpCopyText => BuildSubmittedWriteUpCopyText(SubmittedWriteUpDisplay, SubmittedAt);

        private static string BuildSubmittedWriteUpCopyText(
            string? writeUpText,
            DateTime? submittedAt)
        {
            var text =
                writeUpText?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(text) ||
                !submittedAt.HasValue)
            {
                return text;
            }

            var submittedLine =
                $"Submitted: {submittedAt.Value:MM/dd/yyyy HH:mm}";

            var lines =
                text.Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.None)
                    .ToList();

            var reasonIndex =
                lines.FindIndex(
                    line =>
                        line.TrimStart().StartsWith(
                            "Reason:",
                            StringComparison.OrdinalIgnoreCase));

            if (reasonIndex >= 0)
            {
                /*
                 * Keep the submission timestamp immediately above
                 * the Reason line in the copied write-up.
                 */
                lines.Insert(
                    reasonIndex,
                    submittedLine);
            }
            else
            {
                /*
                 * Older write-ups may not contain a Reason line.
                 * Still include the submission timestamp.
                 */
                lines.Insert(
                    0,
                    submittedLine);
            }

            return string.Join(
                Environment.NewLine,
                lines);
        }

        private static string BuildDispatchWriteUpDisplay(string? value)
        {
            var text = value?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(text))
                return "";

            const string ticketHeader =
                "------------Ticket------------";

            var ticketStart =
                text.IndexOf(
                    ticketHeader,
                    StringComparison.OrdinalIgnoreCase);

            if (ticketStart < 0)
                return text;

            /*
             * The Site History version intentionally contains a Ticket
             * reference section. Dispatch already has the ticket fields in
             * the row above, so omit that duplicated section here.
             *
             * Preserve the CNP Techs footer when one follows the Ticket block.
             */
            var techFooterStart =
                text.IndexOf(
                    "CNP Techs:",
                    ticketStart,
                    StringComparison.OrdinalIgnoreCase);

            var beforeTicket =
                text[..ticketStart].TrimEnd();

            if (techFooterStart < 0)
                return beforeTicket;

            var techFooter =
                text[techFooterStart..].TrimStart();

            if (string.IsNullOrWhiteSpace(beforeTicket))
                return techFooter;

            return beforeTicket +
               Environment.NewLine +
               "------------------------------" +
               Environment.NewLine +
               techFooter;
        }

        public List<string> WriteUpFlags { get; set; } = new();

        public List<string> ReferToOptions { get; set; } = new();

        public List<DispatchCloseoutChecklistItem> CloseoutChecklistItems { get; set; } = new();

        public int ChecklistTotalCount => CloseoutChecklistItems?.Count ?? 0;

        public int ChecklistCompletedCount =>
            CloseoutChecklistItems?.Count(
                x => x.IsCompleted) ?? 0;

        public string ChecklistProgressDisplay
        {
            get
            {
                var total =
                    ChecklistTotalCount;

                if (total == 0)
                    return "—";

                return $"{ChecklistCompletedCount} of {total} complete";
            }
        }

        public void RefreshChecklistProgress()
        {
            OnPropertyChanged(
                nameof(ChecklistTotalCount));

            OnPropertyChanged(
                nameof(ChecklistCompletedCount));

            OnPropertyChanged(
                nameof(ChecklistProgressDisplay));
        }

        private int _requiredChecklistRemaining;

        public int RequiredChecklistRemaining
        {
            get => _requiredChecklistRemaining;
            set
            {
                if (_requiredChecklistRemaining == value)
                    return;

                _requiredChecklistRemaining = value;
                OnPropertyChanged();
            }
        }

        private bool _canMarkClosed = true;

        public bool CanMarkClosed
        {
            get => _canMarkClosed;
            set
            {
                if (_canMarkClosed == value)
                    return;

                _canMarkClosed = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(CanCloseFromTasks));
            }
        }

        public bool CanCloseFromTasks => CanMarkClosed && !IsClosed;

        public string WriteUpFlagsDisplay => WriteUpFlags.Count == 0
            ? ""
            : string.Join(Environment.NewLine, WriteUpFlags);

        public string ReferToOptionsDisplay => ReferToOptions.Count == 0
            ? ""
            : string.Join(" • ", ReferToOptions);
    }
}