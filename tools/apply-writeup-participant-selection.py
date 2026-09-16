from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8-sig")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}")
    file_path.write_text(text.replace(old, new, 1), encoding="utf-8")


# Contracts: clarify that an empty selection is legacy/backward-compatible behavior.
replace_once(
    "SmartGridSuite.Contracts/Tickets/SubmitTicketWriteUpRequest.cs",
    '''        /*
         * Technicians selected in the Submit Write-Up preview.
         *
         * All crew technicians will be selected by default. The submitting
         * technician may remove unavailable technicians before confirming.
         */
        public List<SubmitTicketWriteUpTechnician> SelectedTechnicians { get; set; } = new();
''',
    '''        /*
         * Technicians explicitly selected in the Submit Write-Up preview.
         *
         * New clients include the submitting technician plus every checked crew
         * member. An empty list preserves the legacy server-side crew resolution
         * used by older clients.
         */
        public List<SubmitTicketWriteUpTechnician> SelectedTechnicians { get; set; } = new();
''')


# API: filter the authoritative resolved participant list BEFORE footer, Site History,
# personal History, assignment completion, or email are generated.
replace_once(
    "SmartGridSuite.Api/Controllers/TicketsController.cs",
    '''                var submittedWork = await ResolveSubmittedWorkAsync(
                    entity,
                    req.SubmittedBy,
                    submittedAt.Date,
                    ct);

                /*
''',
    '''                var submittedWork = await ResolveSubmittedWorkAsync(
                    entity,
                    req.SubmittedBy,
                    submittedAt.Date,
                    ct);

                submittedWork = ApplyRequestedParticipantSelection(
                    submittedWork,
                    req.SelectedTechnicians);

                /*
''')

participant_helper = r'''        // Applies the technician choices made in Submit Write-Up Preview to the
        // API-resolved crew snapshot. The client may only remove resolved crew members;
        // it cannot inject an unrelated technician. The submitter is always retained.
        //
        // An empty list intentionally means "legacy client" and leaves the original
        // server-side participant resolution unchanged.
        private static SubmittedWorkInfo ApplyRequestedParticipantSelection(
            SubmittedWorkInfo submittedWork,
            IReadOnlyCollection<SubmitTicketWriteUpTechnician>? selectedTechnicians)
        {
            var requested =
                (selectedTechnicians ?? Array.Empty<SubmitTicketWriteUpTechnician>())
                    .Where(x => x != null)
                    .ToList();

            if (requested.Count == 0)
                return submittedWork;

            static bool NamesMatch(string? first, string? second)
            {
                return !string.IsNullOrWhiteSpace(first) &&
                       !string.IsNullOrWhiteSpace(second) &&
                       string.Equals(
                           first.Trim(),
                           second.Trim(),
                           StringComparison.OrdinalIgnoreCase);
            }

            bool IsRequested(SubmittedParticipantInfo participant)
            {
                if (participant.IsSubmitter)
                    return true;

                foreach (var selected in requested)
                {
                    if (selected.TechnicianId.HasValue &&
                        participant.TechnicianId.HasValue &&
                        selected.TechnicianId.Value == participant.TechnicianId.Value)
                    {
                        return true;
                    }

                    if (EmployeeIdsMatch(
                            selected.EmployeeId,
                            participant.EmployeeId))
                    {
                        return true;
                    }

                    if (NamesMatch(
                            selected.TechnicianName,
                            participant.TechnicianName))
                    {
                        return true;
                    }
                }

                return false;
            }

            var filteredParticipants =
                submittedWork.Participants
                    .Where(IsRequested)
                    .OrderByDescending(x => x.IsSubmitter)
                    .ThenBy(x => x.TechnicianName)
                    .ToList();

            /*
             * ResolveSubmittedWorkAsync always supplies a submitter, but keep this
             * defensive fallback so a malformed client selection can never remove
             * the person actually submitting the write-up.
             */
            if (!filteredParticipants.Any(x => x.IsSubmitter))
            {
                var submitter =
                    submittedWork.Participants
                        .FirstOrDefault(x => x.IsSubmitter);

                if (submitter != null)
                {
                    filteredParticipants.Insert(
                        0,
                        submitter);
                }
            }

            var secondaryNames =
                filteredParticipants
                    .Where(x => !x.IsSubmitter)
                    .Select(x => x.TechnicianName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

            return new SubmittedWorkInfo
            {
                SubmittedByTechnicianId =
                    submittedWork.SubmittedByTechnicianId,

                SubmittedByEmployeeId =
                    submittedWork.SubmittedByEmployeeId,

                SubmittedByName =
                    submittedWork.SubmittedByName,

                PrimaryTech =
                    submittedWork.PrimaryTech,

                SecondaryTech =
                    secondaryNames.Count == 0
                        ? null
                        : FormatCrewDisplayText(secondaryNames),

                Participants =
                    filteredParticipants
            };
        }

'''

replace_once(
    "SmartGridSuite.Api/Controllers/TicketsController.cs",
    '''        private static bool EmployeeIdsMatch(
''',
    participant_helper + '''        private static bool EmployeeIdsMatch(
''')


# Workspace: use the participant-aware preview and carry its selected technicians
# with the submission event.
replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardWorkspaceView/SiteDashboardWorkspaceView.WriteUps.cs",
    '''            var confirmed = ShowWriteUpPreviewWindow(finalWriteUpText);

            if (!confirmed)
                return;

            WriteUpSubmitRequested?.Invoke(
                this,
                new WriteUpSubmitRequestedEventArgs(
                    finalWriteUpText,
                    siteHistoryWriteUpText,
                    true,
                    IncludePingStatsCheckBox.IsChecked == true,
                    IncludeSnmpStatsCheckBox.IsChecked == true,
                    GetSelectedWriteUpFlagIds(),
                    GetSelectedReferToOptionIds()));
''',
    '''            var preview =
                ShowWriteUpPreviewWindowWithParticipants(
                    finalWriteUpText,
                    siteHistoryWriteUpText);

            if (preview is null)
                return;

            WriteUpSubmitRequested?.Invoke(
                this,
                new WriteUpSubmitRequestedEventArgs(
                    preview.FinalWriteUpText,
                    preview.SiteHistoryWriteUpText,
                    true,
                    IncludePingStatsCheckBox.IsChecked == true,
                    IncludeSnmpStatsCheckBox.IsChecked == true,
                    GetSelectedWriteUpFlagIds(),
                    GetSelectedReferToOptionIds(),
                    preview.SelectedTechnicians));
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardWorkspaceView/SiteDashboardWorkspaceView.WriteUps.cs",
    '''                bool includeSnmpStats,
                IReadOnlyCollection<uint>? writeUpFlagIds = null,
                IReadOnlyCollection<uint>? referToOptionIds = null)
''',
    '''                bool includeSnmpStats,
                IReadOnlyCollection<uint>? writeUpFlagIds = null,
                IReadOnlyCollection<uint>? referToOptionIds = null,
                IReadOnlyCollection<SmartGridSuite.Contracts.Tickets.SubmitTicketWriteUpTechnician>? selectedTechnicians = null)
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardWorkspaceView/SiteDashboardWorkspaceView.WriteUps.cs",
    '''                ReferToOptionIds =
                    (referToOptionIds ?? Array.Empty<uint>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();
            }

            public string FinalWriteUpText { get; }
''',
    '''                ReferToOptionIds =
                    (referToOptionIds ?? Array.Empty<uint>())
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                SelectedTechnicians =
                    (selectedTechnicians ??
                     Array.Empty<SmartGridSuite.Contracts.Tickets.SubmitTicketWriteUpTechnician>())
                    .Where(x => x != null)
                    .ToList();
            }

            public string FinalWriteUpText { get; }
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardWorkspaceView/SiteDashboardWorkspaceView.WriteUps.cs",
    '''            public IReadOnlyList<uint> WriteUpFlagIds { get; }
            public IReadOnlyList<uint> ReferToOptionIds { get; }
''',
    '''            public IReadOnlyList<uint> WriteUpFlagIds { get; }
            public IReadOnlyList<uint> ReferToOptionIds { get; }
            public IReadOnlyList<SmartGridSuite.Contracts.Tickets.SubmitTicketWriteUpTechnician> SelectedTechnicians { get; }
''')


# Tickets API: include the selected technician snapshot in the write-up POST.
replace_once(
    "SmartGridSuite.Client/Services/TicketsApi.cs",
    '''            IReadOnlyCollection<uint>? writeUpFlagIds = null,
            IReadOnlyCollection<uint>? referToOptionIds = null,
            bool equipmentWasSwapped = false,
''',
    '''            IReadOnlyCollection<uint>? writeUpFlagIds = null,
            IReadOnlyCollection<uint>? referToOptionIds = null,
            IReadOnlyCollection<SubmitTicketWriteUpTechnician>? selectedTechnicians = null,
            bool equipmentWasSwapped = false,
''')

replace_once(
    "SmartGridSuite.Client/Services/TicketsApi.cs",
    '''                        ReferToOptionIds =
                            new List<uint>(
                                referToOptionIds ??
                                Array.Empty<uint>()),

                        EquipmentWasSwapped = equipmentWasSwapped,
''',
    '''                        ReferToOptionIds =
                            new List<uint>(
                                referToOptionIds ??
                                Array.Empty<uint>()),

                        SelectedTechnicians =
                            new List<SubmitTicketWriteUpTechnician>(
                                selectedTechnicians ??
                                Array.Empty<SubmitTicketWriteUpTechnician>()),

                        EquipmentWasSwapped = equipmentWasSwapped,
''')


# Local draft: persist participant choices so an offline retry submits the exact
# crew selection that the technician confirmed.
replace_once(
    "SmartGridSuite.Client/Services/WriteUpDraftService.cs",
    '''using System.Linq;

namespace SmartGridSuite.Client.Services
''',
    '''using System.Linq;
using SmartGridSuite.Contracts.Tickets;

namespace SmartGridSuite.Client.Services
''')

replace_once(
    "SmartGridSuite.Client/Services/WriteUpDraftService.cs",
    '''        public List<uint> ReferToOptionIds { get; set; } = new();

        public bool EquipmentWasSwapped { get; set; }
''',
    '''        public List<uint> ReferToOptionIds { get; set; } = new();

        public List<SubmitTicketWriteUpTechnician> SelectedTechnicians { get; set; } = new();

        public bool EquipmentWasSwapped { get; set; }
''')


# Pane submission flow: carry selections from preview -> API and from pending JSON -> retry.
replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardPaneView/SiteDashboardPaneView.WriteUps.cs",
    '''using SmartGridSuite.Contracts.Crews;
using System;
''',
    '''using SmartGridSuite.Contracts.Crews;
using SmartGridSuite.Contracts.Tickets;
using System;
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardPaneView/SiteDashboardPaneView.WriteUps.cs",
    '''            var referToOptionIds =
                new List<uint>(
                    e.ReferToOptionIds ??
                    Array.Empty<uint>());

            var equipmentWasSwapped =
''',
    '''            var referToOptionIds =
                new List<uint>(
                    e.ReferToOptionIds ??
                    Array.Empty<uint>());

            var selectedTechnicians =
                new List<SubmitTicketWriteUpTechnician>(
                    e.SelectedTechnicians ??
                    Array.Empty<SubmitTicketWriteUpTechnician>());

            var equipmentWasSwapped =
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardPaneView/SiteDashboardPaneView.WriteUps.cs",
    '''                    referToOptionIds =
                         new List<uint>(
                             pendingDraft.ReferToOptionIds ??
                             new List<uint>());

                    equipmentWasSwapped =
''',
    '''                    referToOptionIds =
                         new List<uint>(
                             pendingDraft.ReferToOptionIds ??
                             new List<uint>());

                    selectedTechnicians =
                        new List<SubmitTicketWriteUpTechnician>(
                            pendingDraft.SelectedTechnicians ??
                            new List<SubmitTicketWriteUpTechnician>());

                    equipmentWasSwapped =
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardPaneView/SiteDashboardPaneView.WriteUps.cs",
    '''                    writeUpFlagIds: writeUpFlagIds,
                    referToOptionIds: referToOptionIds,
                    equipmentWasSwapped: equipmentWasSwapped,
''',
    '''                    writeUpFlagIds: writeUpFlagIds,
                    referToOptionIds: referToOptionIds,
                    selectedTechnicians: selectedTechnicians,
                    equipmentWasSwapped: equipmentWasSwapped,
''')

replace_once(
    "SmartGridSuite.Client/Views/Dispatcher/Panes/SiteDashboard/SiteDashboardPaneView/SiteDashboardPaneView.WriteUps.cs",
    '''                draft.ReferToOptionIds =
                    new List<uint>(
                        submission.ReferToOptionIds ??
                        Array.Empty<uint>());

                draft.EquipmentWasSwapped =
''',
    '''                draft.ReferToOptionIds =
                    new List<uint>(
                        submission.ReferToOptionIds ??
                        Array.Empty<uint>());

                draft.SelectedTechnicians =
                    new List<SubmitTicketWriteUpTechnician>(
                        submission.SelectedTechnicians ??
                        Array.Empty<SubmitTicketWriteUpTechnician>());

                draft.EquipmentWasSwapped =
''')

print("Write-up participant selection patches applied successfully.")
