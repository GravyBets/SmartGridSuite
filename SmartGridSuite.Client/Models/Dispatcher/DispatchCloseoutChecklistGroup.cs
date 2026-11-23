using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SmartGridSuite.Client.Models.Dispatcher
{
    // One historical technician submission; items remain independently editable.
    public sealed class DispatchCloseoutChecklistGroup : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public long SubmissionId { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string SubmittedByName { get; set; } = "";
        public int Sequence { get; set; }
        public List<DispatchCloseoutChecklistItem> Items { get; set; } = new();

        public string Header =>
            $"Write-Up #{Sequence} — " +
            (string.IsNullOrWhiteSpace(SubmittedByName) ? "Unknown technician" : SubmittedByName) +
            (SubmittedAt.HasValue ? $" • {SubmittedAt.Value:MM/dd/yyyy HH:mm}" : "");

        public string ProgressDisplay =>
            $"{Items.Count(x => x.IsCompleted)} of {Items.Count} complete";

        public void RefreshProgress([CallerMemberName] string? _ = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressDisplay)));
    }
}
