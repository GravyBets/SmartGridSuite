#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Crews;
using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using static SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard.SiteDashboardWorkspaceView;
using System.Collections.Generic;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private readonly WriteUpDraftService _writeUpDraftService = new();

        /*
         * Prevents repeated disk searches every time the same dashboard tab re-renders.
         * A failed submission clears the checked flag so the new pending file is found
         * when the technician next returns to that tab.
         */
        private readonly HashSet<string>
            _pendingWriteUpCheckedSessionKeys =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string>
            _pendingWriteUpLoadingSessionKeys =
                new(StringComparer.OrdinalIgnoreCase);

        // Prevents the same reopened site tab from showing the pending-write-up
        // message more than once when the dashboard renders in multiple stages.
        private readonly HashSet<string>
            _pendingWriteUpPromptedSessionKeys =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, WriteUpDraftRecord>
            _pendingWriteUpDraftsBySessionKey =
                new(StringComparer.OrdinalIgnoreCase);

        // Keeps the current write-up text in its Site Dashboard tab session.
        // No disk access, API calls, or connectivity checks occur while typing.
        private void WorkspaceView_WriteUpTextChanged(object? sender, string text)
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();

            if (session is null)
                return;

            session.WriteUpText = text ?? string.Empty;
        }

        // Submits a new write-up or safely retries a previously saved local
        // submission using its original idempotency key and confirmed payload.
        private async void WorkspaceView_WriteUpSubmitRequested(object? sender, WriteUpSubmitRequestedEventArgs e)
        {
            if (_writeUpSubmitInProgress)
            {
                TopBarView.StatusText =
                    "Write-up submit already running...";

                return;
            }

            var session = GetSelectedSession();

            if (session is null)
                return;

            var employeeId =
                GetWindowsEmployeeId();

            var siteKey =
                ResolveWriteUpDraftSiteKey(session);

            var sessionKey =
                (session.SessionKey ?? string.Empty).Trim();

            WriteUpDraftRecord? pendingDraft = null;

            var isPendingRetry = false;
            var submissionConfirmed = false;

            var clientSubmissionId =
                Guid.Empty;

            var finalWriteUpText =
                e.FinalWriteUpText ?? string.Empty;

            var siteHistoryWriteUpText =
                e.SiteHistoryWriteUpText ?? string.Empty;

            var writeUpFlagIds =
                new List<uint>(
                    e.WriteUpFlagIds ??
                    Array.Empty<uint>());

            var referToOptionIds =
                new List<uint>(
                    e.ReferToOptionIds ??
                    Array.Empty<uint>());

            var equipmentWasSwapped =
                WorkspaceView
                    .GetEquipmentReplacementSessionEntries()
                    .Count > 0;

            var ipAddressWasChanged =
                NetworkView.HasIpAddressChanges(
                    session.PrimaryIp,
                    session.LanIp,
                    session.SecondaryIp,
                    session.IgsdPrimaryRtuIp,
                    session.IgsdPrimaryCommsEthernetIp,
                    session.IgsdSecondaryCommsEthernetIp,
                    session.IgsdSecondaryRtuIp);

            long? originalTicketId = session.CurrentTicketId > 0
                    ? session.CurrentTicketId
                    : null;

            long? submittedTicketId = null;

            try
            {
                _writeUpSubmitInProgress = true;

                ShowSiteLoadOverlay(
                    "Submitting write-up...");

                TopBarView.StatusText =
                    "Submitting write-up...";

                await Task.Yield();

                /*
                 * First check for a pending draft already restored into this
                 * dashboard session.
                 */
                if (!string.IsNullOrWhiteSpace(sessionKey))
                {
                    _pendingWriteUpDraftsBySessionKey.TryGetValue(
                        sessionKey,
                        out pendingDraft);
                }

                /*
                 * Also check the local JSON directly. This allows Retry to work
                 * even when the dashboard tab was never closed and reopened.
                 */
                if (pendingDraft?.IsPendingSubmission != true)
                {
                    var preferredTicketId =
                        session.CurrentTicketId > 0
                            ? session.CurrentTicketId
                            : (long?)null;

                    pendingDraft =
                        await _writeUpDraftService.FindPendingDraftAsync(
                            employeeId,
                            siteKey,
                            preferredTicketId,
                            CancellationToken.None);

                    if (pendingDraft?.IsPendingSubmission == true &&
                        !string.IsNullOrWhiteSpace(sessionKey))
                    {
                        _pendingWriteUpDraftsBySessionKey[sessionKey] =
                            pendingDraft;
                    }
                }

                isPendingRetry =
                    pendingDraft?.IsPendingSubmission == true;

                if (isPendingRetry)
                {
                    /*
                     * Retry the exact previously confirmed payload using the same
                     * ClientSubmissionId. This prevents duplicate Site History
                     * records when the original request succeeded but its response
                     * was lost.
                     */
                    if (pendingDraft!.ClientSubmissionId == Guid.Empty)
                    {
                        throw new InvalidOperationException(
                            "The pending write-up is missing its submission ID and cannot be retried safely.");
                    }

                    clientSubmissionId =
                        pendingDraft.ClientSubmissionId;

                    finalWriteUpText =
                        pendingDraft.FinalWriteUpText ??
                        string.Empty;

                    siteHistoryWriteUpText =
                        pendingDraft.SiteHistoryWriteUpText ??
                        string.Empty;

                    writeUpFlagIds =
                        new List<uint>(
                            pendingDraft.WriteUpFlagIds ??
                            new List<uint>());

                    referToOptionIds =
                         new List<uint>(
                             pendingDraft.ReferToOptionIds ??
                             new List<uint>());

                    equipmentWasSwapped =
                        pendingDraft.EquipmentWasSwapped;

                    ipAddressWasChanged =
                        pendingDraft.IpAddressWasChanged;

                    if (pendingDraft.TicketId.HasValue && pendingDraft.TicketId.Value > 0)
                    {
                        originalTicketId =
                            pendingDraft.TicketId;
                    }

                    UpdateSiteLoadOverlayMessage(
                        "Retrying pending write-up...");

                    TopBarView.StatusText =
                        "Retrying pending write-up...";
                }
                else
                {
                    /*
                     * Brand-new submissions receive a new idempotency key.
                     */
                    clientSubmissionId =
                        Guid.NewGuid();
                }

                var pendingTicketId =
                    pendingDraft?.TicketId ?? 0;

                var targetTicketId =
                    isPendingRetry && pendingTicketId > 0
                        ? pendingTicketId
                        : session.CurrentTicketId;

                UpdateSiteLoadOverlayMessage(
                    "Finding or creating ticket for write-up...");

                if (targetTicketId <= 0)
                {
                    targetTicketId =
                        await _ticketsApi.RequestTicketAsync(
                            session.HeaderText,
                            "Write-up submitted from Site Dashboard with no associated ticket.",
                            requestedBy: employeeId,
                            CancellationToken.None);

                    session.CurrentTicketId =
                        targetTicketId;
                }

                if (targetTicketId <= 0)
                {
                    TopBarView.StatusText =
                        "Write-up submit failed: no ticket could be created or found.";

                    return;
                }

                UpdateSiteLoadOverlayMessage(
                    "Submitting write-up to server...");

                await _ticketsApi.SubmitWriteUpAsync(
                    targetTicketId,
                    clientSubmissionId,
                    finalWriteUpText,
                    siteHistoryWriteUpText,
                    submittedBy: employeeId,
                    writeUpFlagIds: writeUpFlagIds,
                    referToOptionIds: referToOptionIds,
                    equipmentWasSwapped: equipmentWasSwapped,
                    ipAddressWasChanged: ipAddressWasChanged,
                    ct: CancellationToken.None);

                /*
                 * The API has now committed the write-up and changed the ticket
                 * status. No later local cleanup or UI refresh problem may be
                 * treated as a failed submission.
                 */
                submissionConfirmed = true;

                session.CurrentTicketId =
                    targetTicketId;

                submittedTicketId =
                    targetTicketId;

                UpdateSiteLoadOverlayMessage(
                    "Cleaning up local pending write-up backup...");

                /*
                 * Remove local JSON only after the API positively confirms success.
                 * This helper already treats local deletion failures as non-fatal.
                 */
                await DeletePendingWriteUpDraftsAfterSuccessAsync(
                    employeeId,
                    siteKey,
                    originalTicketId,
                    submittedTicketId);

                /*
                 * Remove restored in-memory draft state so the successful
                 * submission cannot be offered as pending again.
                 */
                ResetPendingWriteUpRecoveryCheck(
                    session);

                /*
                 * Clear the submitted write-up from both the saved dashboard session
                 * and the currently visible TextBox.
                 */
                session.WriteUpText =
                    string.Empty;

                session.WriteUpText =
                    string.Empty;

                /*
                 * The submitted flags and Refer To destinations belong only to this
                 * completed write-up. Do not restore them on the next render.
                 */
                session.SubmitOptions.WriteUpFlagIds.Clear();
                session.SubmitOptions.ReferToOptionIds.Clear();

                if (session.SessionKey ==
                    _selectedSessionKey)
                {
                    WorkspaceView.WriteUpText =
                        string.Empty;

                    WorkspaceView.ClearWriteUpWorkflowSelections();
                }

                /*
                 * Refresh only SmartGridSuite-owned information. Do not reload the
                 * full dashboard because that can invoke Parent DB lookup logic.
                 */
                UpdateSiteLoadOverlayMessage(
                    "Refreshing ticket status and site history...");

                TopBarView.StatusText =
                    "Refreshing ticket status and site history...";

                try
                {
                    await RefreshTicketInfoAsync(
                        session,
                        CancellationToken.None);

                    await LoadSiteHistoryForSessionAsync(
                        session,
                        CancellationToken.None);

                    session.SelectedWorkspaceTabKey =
                        "SiteHistory";

                    if (session.SessionKey ==
                        _selectedSessionKey)
                    {
                        RenderSelectedSession();
                    }

                    TopBarView.StatusText =
                        isPendingRetry
                            ? "Pending write-up submitted successfully."
                            : "Write-up submitted successfully.";
                }
                catch
                {
                    /*
                     * The API already committed the write-up. Failure to reload ticket
                     * information or Site History must not recreate a pending draft.
                     */
                    session.SelectedWorkspaceTabKey =
                        "SiteHistory";

                    if (session.SessionKey ==
                        _selectedSessionKey)
                    {
                        RenderSelectedSession();
                    }

                    TopBarView.StatusText =
                        "Write-up submitted successfully. Ticket status or Site History will refresh when the screen is reloaded.";
                }
            }
            catch (ApiClient.ApiConnectionException)
            {
                if (submissionConfirmed)
                {
                    TopBarView.StatusText =
                        "Write-up submitted successfully. Ticket status will refresh when the screen is reloaded.";

                    return;
                }

                UpdateSiteLoadOverlayMessage(
                    "Connection failed. Saving write-up locally...");

                bool savedLocally;

                if (isPendingRetry &&
                    pendingDraft is not null)
                {
                    /*
                     * Connectivity is still unavailable. Preserve the same pending
                     * payload and idempotency key for another retry.
                     */
                    try
                    {
                        await _writeUpDraftService.SaveDraftAsync(
                            pendingDraft,
                            CancellationToken.None);

                        savedLocally = true;
                    }
                    catch
                    {
                        savedLocally = false;
                    }
                }
                else
                {
                    /*
                     * This was a new submission that never received API
                     * confirmation. Save its exact payload and submission ID.
                     */
                    savedLocally =
                        await TrySavePendingWriteUpDraftAsync(
                            session,
                            e,
                            employeeId,
                            siteKey,
                            clientSubmissionId,
                            equipmentWasSwapped,
                            ipAddressWasChanged);

                    if (savedLocally)
                    {
                        ResetPendingWriteUpRecoveryCheck(
                            session);
                    }
                }

                if (savedLocally)
                {
                    TopBarView.StatusText =
                        isPendingRetry
                            ? "Still offline — the pending write-up remains saved locally."
                            : "Offline — write-up saved locally but has not been submitted.";

                    MessageBox.Show(
                        Window.GetWindow(this),
                        isPendingRetry
                            ? "The pending write-up still could not be submitted because the Smart Grid Suite server is unavailable.\n\n" +
                              "The write-up remains saved safely on this computer.\n\n" +
                              "Restore network connectivity and submit the write-up again."
                            : "The write-up could not be submitted because the Smart Grid Suite server is unavailable.\n\n" +
                              "A copy of the write-up has been saved safely on this computer.\n\n" +
                              "When the network connection is restored, reopen this site. The saved write-up will be restored automatically and must be submitted again.",
                        isPendingRetry
                            ? "Write-Up Retry Failed"
                            : "Write-Up Submission Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    TopBarView.StatusText =
                        "The write-up was not submitted, and the local backup could not be saved.";

                    MessageBox.Show(
                        Window.GetWindow(this),
                        "The write-up could not be submitted because the Smart Grid Suite server is unavailable.\n\n" +
                        "The application was also unable to save a local backup.\n\n" +
                        "Do not close this site tab or exit Smart Grid Suite. Copy the write-up text somewhere safe before continuing.",
                        "Write-Up Submission and Backup Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (ApiClient.ApiException ex)
            {
                if (submissionConfirmed)
                {
                    TopBarView.StatusText =
                        "Write-up submitted successfully. Ticket status will refresh when the screen is reloaded.";

                    return;
                }

                /*
                 * The API was reachable but rejected the submission itself.
                 * Preserve any existing pending JSON for investigation or retry.
                 */
                TopBarView.StatusText =
                    $"Write-up submit failed: server error {ex.StatusCode}. " +
                    ex.Message;
            }
            catch (Exception ex)
            {
                if (submissionConfirmed)
                {
                    TopBarView.StatusText =
                        "Write-up submitted successfully. A local screen refresh or cleanup operation failed.";

                    return;
                }

                TopBarView.StatusText =
                    $"Write-up submit failed: {ex.Message}";
            }
            finally
            {
                _writeUpSubmitInProgress = false;
                HideSiteLoadOverlay();
            }
        }

        // Saves the exact failed submission, including the same idempotency key used
        // during the original API attempt.
        private async Task<bool> TrySavePendingWriteUpDraftAsync(
            SiteDashboardTabSession session,
            WriteUpSubmitRequestedEventArgs submission,
            string employeeId,
            string siteKey,
            Guid clientSubmissionId,
            bool equipmentWasSwapped,
            bool ipAddressWasChanged)
        {
            try
            {
                var ticketId = session.CurrentTicketId > 0
                    ? session.CurrentTicketId
                    : (long?)null;

                var existingDraft =
                    await _writeUpDraftService.LoadDraftAsync(
                        employeeId,
                        ticketId,
                        siteKey,
                        CancellationToken.None);

                var draft =
                    existingDraft ??
                    new WriteUpDraftRecord
                    {
                        EmployeeId = employeeId,
                        TicketId = ticketId,
                        SiteKey = siteKey,
                        ClientSubmissionId = clientSubmissionId
                    };

                draft.EmployeeId = employeeId;
                draft.TicketId = ticketId;
                draft.SiteKey = siteKey;
                draft.ClientSubmissionId = clientSubmissionId;

                draft.ManualWriteUpText =
                    session.WriteUpText ?? string.Empty;

                draft.FinalWriteUpText =
                    submission.FinalWriteUpText ?? string.Empty;

                draft.SiteHistoryWriteUpText =
                    submission.SiteHistoryWriteUpText ?? string.Empty;

                draft.WriteUpFlagIds =
                    new List<uint>(
                        submission.WriteUpFlagIds ??
                        Array.Empty<uint>());

                draft.ReferToOptionIds =
                    new List<uint>(
                        submission.ReferToOptionIds ??
                        Array.Empty<uint>());

                draft.EquipmentWasSwapped =
                    equipmentWasSwapped;

                draft.IpAddressWasChanged =
                    ipAddressWasChanged;

                draft.IsPendingSubmission = true;

                await _writeUpDraftService.SaveDraftAsync(
                    draft,
                    CancellationToken.None);

                return true;
            }
            catch
            {
                /*
                 * Local backup failure is reported through the status bar rather
                 * than escaping the async event handler and crashing WPF.
                 */
                return false;
            }
        }

        // Removes local pending copies only after the API confirms submission.
        // Multiple keys are checked because a ticket may have been created
        // immediately before the connection was lost during an earlier attempt.
        private async Task DeletePendingWriteUpDraftsAfterSuccessAsync(
            string employeeId,
            string siteKey,
            long? originalTicketId,
            long? submittedTicketId)
        {
            var candidateTicketIds = new long?[]
                {
                    null,
                    originalTicketId,
                    submittedTicketId
                }
                .Distinct()
                .ToList();

            foreach (var ticketId in candidateTicketIds)
            {
                try
                {
                    await _writeUpDraftService.DeleteDraftAsync(
                        employeeId,
                        ticketId,
                        siteKey,
                        CancellationToken.None);
                }
                catch
                {
                    /*
                     * The server submission already succeeded. A local file
                     * cleanup problem must not be reported as a failed write-up.
                     */
                }
            }
        }

        // Produces a stable local identifier for loaded sites and temporary
        // blank dashboard tabs that do not yet have an associated ticket.
        private static string ResolveWriteUpDraftSiteKey(
            SiteDashboardTabSession session)
        {
            var siteKey =
                (session.SearchText ??
                 session.HeaderText ??
                 string.Empty)
                .Trim();

            if (!string.IsNullOrWhiteSpace(siteKey) &&
                !siteKey.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase))
            {
                return siteKey;
            }

            var sessionKey =
                (session.SessionKey ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(sessionKey)
                ? "UNKNOWN-SITE"
                : sessionKey;
        }
        
        // Starts one pending-write-up lookup for the current combination of dashboard
        // session, loaded site, and ticket. A session can render before its site data
        // finishes loading, so SessionKey alone is not specific enough.
        private void QueuePendingWriteUpRecoveryForRenderedSession(SiteDashboardTabSession session)
        {
            var sessionKey =
                (session.SessionKey ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(sessionKey))
                return;

            var siteKey =
                ResolveWriteUpDraftSiteKey(session);

            /*
             * Do not permanently mark the initial empty/blank dashboard render as
             * checked. Recovery will run after a real site is loaded into this session.
             */
            if (string.IsNullOrWhiteSpace(siteKey) ||
                string.Equals(
                    siteKey,
                    "UNKNOWN-SITE",
                    StringComparison.OrdinalIgnoreCase) ||
                siteKey.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var ticketKey =
                session.CurrentTicketId > 0
                    ? session.CurrentTicketId.ToString()
                    : "NO-TICKET";

            var lookupKey =
                $"{sessionKey}|{siteKey}|{ticketKey}";

            if (_pendingWriteUpCheckedSessionKeys.Contains(lookupKey) ||
                _pendingWriteUpLoadingSessionKeys.Contains(lookupKey))
            {
                return;
            }

            _pendingWriteUpLoadingSessionKeys.Add(
                lookupKey);

            _ = LoadPendingWriteUpRecoveryAsync(
                session,
                sessionKey,
                lookupKey);
        }

        // Loads a pending local submission, restores its manual write-up text, and
        // clearly warns that the write-up has not reached the server.
        private async Task LoadPendingWriteUpRecoveryAsync(SiteDashboardTabSession session, string sessionKey, string lookupKey)
        {
            try
            {
                var employeeId =
                    GetWindowsEmployeeId();

                var siteKey =
                    ResolveWriteUpDraftSiteKey(session);

                var preferredTicketId =
                    session.CurrentTicketId > 0
                        ? session.CurrentTicketId
                        : (long?)null;

                var draft =
                    await _writeUpDraftService.FindPendingDraftAsync(
                        employeeId,
                        siteKey,
                        preferredTicketId,
                        CancellationToken.None);

                _pendingWriteUpCheckedSessionKeys.Add(
                    lookupKey);

                if (draft?.IsPendingSubmission != true)
                    return;

                _pendingWriteUpDraftsBySessionKey[sessionKey] =
                    draft;

                /*
                 * Never overwrite text entered after the tab was opened. Normally this
                 * newly reopened session is empty, so the local text is restored.
                 */
                if (string.IsNullOrWhiteSpace(session.WriteUpText) &&
                    !string.IsNullOrWhiteSpace(draft.ManualWriteUpText))
                {
                    session.WriteUpText =
                        draft.ManualWriteUpText;
                }

                if (session.SessionKey != _selectedSessionKey)
                    return;

                _renderingSession = true;

                try
                {
                    WorkspaceView.WriteUpText =
                        session.WriteUpText;
                }
                finally
                {
                    _renderingSession = false;
                }

                TopBarView.StatusText =
                    "Pending write-up restored from this computer. It has not been submitted.";

                /*
                 * The dashboard may render once before ticket information is loaded and again
                 * afterward. Both renders can locate the same JSON, but only the first should
                 * display the recovery message for this opened site tab.
                 */
                if (_pendingWriteUpPromptedSessionKeys.Add(sessionKey))
                {
                    MessageBox.Show(
                        Window.GetWindow(this),
                        "A write-up for this site was saved locally after an unsuccessful submission.\n\n" +
                        "The write-up has now been restored, but it has not reached the server.\n\n" +
                        "Confirm that network connectivity is available, then submit the write-up again.",
                        "Pending Write-Up Restored",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                if (session.SessionKey == _selectedSessionKey)
                {
                    TopBarView.StatusText =
                        $"Unable to restore the pending local write-up: {ex.Message}";
                }
            }
            finally
            {
                _pendingWriteUpLoadingSessionKeys.Remove(
                    lookupKey);
            }
        }

        // Clears all recovery lookups associated with this dashboard session so a new
        // pending JSON can be detected after an unsuccessful submission.
        private void ResetPendingWriteUpRecoveryCheck(SiteDashboardTabSession session)
        {
            var sessionKey =
                (session.SessionKey ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(sessionKey))
                return;

            var keyPrefix =
                sessionKey + "|";

            var checkedKeys =
                _pendingWriteUpCheckedSessionKeys
                    .Where(x => x.StartsWith(
                        keyPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            foreach (var key in checkedKeys)
            {
                _pendingWriteUpCheckedSessionKeys.Remove(
                    key);
            }

            var loadingKeys =
                _pendingWriteUpLoadingSessionKeys
                    .Where(x => x.StartsWith(
                        keyPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            foreach (var key in loadingKeys)
            {
                _pendingWriteUpLoadingSessionKeys.Remove(
                    key);
            }

            _pendingWriteUpDraftsBySessionKey.Remove(
                sessionKey);

            _pendingWriteUpPromptedSessionKeys.Remove(
                sessionKey);
        }

        // Refreshes only SmartGridSuite-owned ticket data after a confirmed write-up.
        // Parent database/dashboard data is intentionally not reloaded here.
        private async Task RefreshDashboardAfterWriteUpSubmitAsync(
            SiteDashboardTabSession session,
            CancellationToken ct)
        {
            await RefreshTicketInfoAsync(
                session,
                ct);

            session.SelectedWorkspaceTabKey =
                "SiteHistory";

            if (session.SessionKey ==
                _selectedSessionKey)
            {
                RenderSelectedSession();
            }
        }

        // Loads the current same-day crew display text used in submitted
        // write-ups, falling back to the Windows employee ID while offline.
        private async Task LoadCurrentCnpTechNameAsync()
        {
            try
            {
                var employeeId =
                    GetWindowsEmployeeId();

                if (string.IsNullOrWhiteSpace(employeeId))
                {
                    _currentCnpTechName =
                        string.Empty;

                    WorkspaceView.CurrentCnpTechName =
                        string.Empty;

                    return;
                }

                var crew =
                    await _api.GetAsync<CurrentCrewDto>(
                        $"api/technicians/current-crew/" +
                        Uri.EscapeDataString(employeeId));

                _currentCnpTechName =
                    string.IsNullOrWhiteSpace(
                        crew?.DisplayText)
                        ? employeeId
                        : crew.DisplayText.Trim();

                WorkspaceView.CurrentCnpTechName =
                    _currentCnpTechName;
            }
            catch
            {
                _currentCnpTechName =
                    GetWindowsEmployeeId();

                WorkspaceView.CurrentCnpTechName =
                    _currentCnpTechName;
            }
        }

        // Extracts the employee ID from the current Windows account.
        private static string GetWindowsEmployeeId()
        {
            var name =
                WindowsIdentity.GetCurrent()?.Name ??
                string.Empty;

            if (name.Contains('\\'))
                name = name.Split('\\').Last();

            if (name.Contains('@'))
                name = name.Split('@').First();

            return name.Trim();
        }
    }
}