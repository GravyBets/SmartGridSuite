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

            var clientSubmissionId =
                Guid.Empty;

            var finalWriteUpText =
                e.FinalWriteUpText ?? string.Empty;

            var siteHistoryWriteUpText =
                e.SiteHistoryWriteUpText ?? string.Empty;

            long? originalTicketId =
                session.CurrentTicketId > 0
                    ? session.CurrentTicketId
                    : null;

            long? submittedTicketId = null;

            try
            {
                _writeUpSubmitInProgress = true;

                TopBarView.StatusText =
                    "Submitting write-up...";

                /*
                 * First check the draft already restored into this dashboard tab.
                 */
                if (!string.IsNullOrWhiteSpace(sessionKey))
                {
                    _pendingWriteUpDraftsBySessionKey.TryGetValue(
                        sessionKey,
                        out pendingDraft);
                }

                /*
                 * Also check the JSON directly. This makes Retry work even when the
                 * site was never closed and reopened after the original failure.
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
                     * Retry the exact previously confirmed submission. Reusing the same
                     * ClientSubmissionId prevents duplicate Site History records when
                     * the original API request succeeded but its response was lost.
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

                    if (pendingDraft.TicketId.HasValue &&
                        pendingDraft.TicketId.Value > 0)
                    {
                        originalTicketId =
                            pendingDraft.TicketId;
                    }

                    TopBarView.StatusText =
                        "Retrying pending write-up...";
                }
                else
                {
                    /*
                     * This is a brand-new submission, so generate a new idempotency key.
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

                await _ticketsApi.SubmitWriteUpAsync(
                    targetTicketId,
                    clientSubmissionId,
                    finalWriteUpText,
                    siteHistoryWriteUpText,
                    submittedBy: employeeId,
                    CancellationToken.None);

                submittedTicketId =
                    targetTicketId;

                /*
                 * Only remove the JSON after the API positively confirms success.
                 */
                await DeletePendingWriteUpDraftsAfterSuccessAsync(
                    employeeId,
                    siteKey,
                    originalTicketId,
                    submittedTicketId);

                /*
                 * Remove the restored in-memory draft and recovery flags so the
                 * successful submission is not shown as pending again.
                 */
                ResetPendingWriteUpRecoveryCheck(
                    session);

                TopBarView.StatusText =
                    "Refreshing site after write-up submit...";

                await RefreshDashboardAfterWriteUpSubmitAsync(
                    session,
                    CancellationToken.None);

                TopBarView.StatusText =
                    isPendingRetry
                        ? "Pending write-up submitted successfully. Site history refreshed."
                        : "Write-up submitted. Site history refreshed.";
            }
            catch (ApiClient.ApiConnectionException)
            {
                bool savedLocally;

                if (isPendingRetry &&
                    pendingDraft is not null)
                {
                    /*
                     * The retry also failed because connectivity is still unavailable.
                     * Save the same draft again without changing its GUID or payload.
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
                     * This was a brand-new submission. Store its exact confirmed
                     * payload and the same GUID used during the failed API attempt.
                     */
                    savedLocally =
                        await TrySavePendingWriteUpDraftAsync(
                            session,
                            e,
                            employeeId,
                            siteKey,
                            clientSubmissionId);

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
                /*
                 * The server was reachable but rejected the request. Keep any pending
                 * JSON so the technician can retry or the issue can be investigated.
                 */
                TopBarView.StatusText =
                    $"Write-up submit failed: server error {ex.StatusCode}. " +
                    ex.Message;
            }
            catch (Exception ex)
            {
                TopBarView.StatusText =
                    $"Write-up submit failed: {ex.Message}";
            }
            finally
            {
                _writeUpSubmitInProgress = false;
            }
        }

        // Saves the exact failed submission, including the same idempotency key used
        // during the original API attempt.
        private async Task<bool> TrySavePendingWriteUpDraftAsync(
            SiteDashboardTabSession session,
            WriteUpSubmitRequestedEventArgs submission,
            string employeeId,
            string siteKey,
            Guid clientSubmissionId)
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

        // Reloads the current dashboard and Site History after the API confirms
        // that a write-up was successfully submitted.
        private async Task RefreshDashboardAfterWriteUpSubmitAsync(
            SiteDashboardTabSession session,
            CancellationToken ct)
        {
            var reloadId =
                (session.SearchText ??
                 session.HeaderText ??
                 string.Empty)
                .Trim();

            if (string.IsNullOrWhiteSpace(reloadId) ||
                reloadId.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase))
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

                return;
            }

            WorkspaceView.StopTowerPings();

            ClearSessionTemporaryDashboardState(
                session);

            try
            {
                var dashboard =
                    await GetSiteOrTowerDashboardAsync(
                        reloadId,
                        ct);

                var loadedSiteId =
                    GetObjectPropertyText(
                        dashboard,
                        "SiteId")
                    ?? reloadId;

                ApplyDashboardToSession(
                    session,
                    dashboard,
                    loadedSiteId);

                if (ShouldLoadSnmpForDashboard(session))
                {
                    await RefreshSnmpConfigAsync(
                        session,
                        ct);
                }
                else
                {
                    ClearSnmpForUnsupportedDashboard(
                        session);
                }
            }
            catch (Exception ex)
                when (IsDashboardNotFoundException(ex))
            {
                /*
                 * Handles new or blank sites that do not exist in the parent
                 * database yet.
                 */
                var blankSiteId =
                    ResolveBlankDashboardSiteId(
                        reloadId);

                ApplyBlankDashboardToSession(
                    session,
                    blankSiteId);

                session.SelectedWorkspaceTabKey =
                    "SiteHistory";
            }

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