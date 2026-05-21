using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Crews;
using System.Security.Principal;
using static SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard.SiteDashboardWorkspaceView;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private void WorkspaceView_WriteUpTextChanged(object? sender, string text)
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();
            if (session is null)
                return;

            session.WriteUpText = text ?? string.Empty;
        }

        private async void WorkspaceView_WriteUpSubmitRequested(object? sender, WriteUpSubmitRequestedEventArgs e)
        {
            if (_writeUpSubmitInProgress)
            {
                TopBarView.StatusText = "Write-up submit already running...";
                return;
            }

            var session = GetSelectedSession();

            if (session is null)
                return;

            try
            {
                _writeUpSubmitInProgress = true;
                TopBarView.StatusText = "Submitting write-up...";

                var targetTicketId = session.CurrentTicketId;

                if (targetTicketId <= 0)
                {
                    targetTicketId = await _ticketsApi.RequestTicketAsync(
                        session.HeaderText,
                        "Write-up submitted from Site Dashboard with no associated ticket.",
                        requestedBy: Environment.UserName,
                        CancellationToken.None);

                    session.CurrentTicketId = targetTicketId;
                }

                if (targetTicketId <= 0)
                {
                    TopBarView.StatusText = "Write-up submit failed: no ticket could be created or found.";
                    return;
                }

                await _ticketsApi.SubmitWriteUpAsync(
                    targetTicketId,
                    e.FinalWriteUpText,
                    e.SiteHistoryWriteUpText,
                    submittedBy: Environment.UserName,
                    CancellationToken.None);

                TopBarView.StatusText = "Refreshing site after write-up submit...";

                await RefreshDashboardAfterWriteUpSubmitAsync(session, CancellationToken.None);

                TopBarView.StatusText = "Write-up submitted. Site history refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Write-up submit failed: {ex.Message}";
            }
            finally
            {
                _writeUpSubmitInProgress = false;
            }
        }

        private async Task RefreshDashboardAfterWriteUpSubmitAsync(SiteDashboardTabSession session, CancellationToken ct)
        {
            var reloadId = (session.SearchText ?? session.HeaderText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(reloadId) ||
                reloadId.StartsWith("Blank", StringComparison.OrdinalIgnoreCase))
            {
                await RefreshTicketInfoAsync(session, ct);

                session.SelectedWorkspaceTabKey = "SiteHistory";

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                return;
            }

            WorkspaceView.StopTowerPings();

            ClearSessionTemporaryDashboardState(session);

            try
            {
                var dashboard = await GetSiteOrTowerDashboardAsync(reloadId, ct);
                var loadedSiteId = GetObjectPropertyText(dashboard, "SiteId") ?? reloadId;

                ApplyDashboardToSession(session, dashboard, loadedSiteId);

                if (ShouldLoadSnmpForDashboard(session))
                {
                    await RefreshSnmpConfigAsync(session, ct);
                }
                else
                {
                    ClearSnmpForUnsupportedDashboard(session);
                }
            }
            catch (Exception ex) when (IsDashboardNotFoundException(ex))
            {
                // This handles brand-new/blank sites that do not exist in the parent DB yet.
                var blankSiteId = ResolveBlankDashboardSiteId(reloadId);

                ApplyBlankDashboardToSession(session, blankSiteId);

                session.SelectedWorkspaceTabKey = "SiteHistory";
            }

            await RefreshTicketInfoAsync(session, ct);

            session.SelectedWorkspaceTabKey = "SiteHistory";

            if (session.SessionKey == _selectedSessionKey)
                RenderSelectedSession();
        }

        private async Task LoadCurrentCnpTechNameAsync()
        {
            try
            {
                var employeeId = GetWindowsEmployeeId();

                if (string.IsNullOrWhiteSpace(employeeId))
                {
                    _currentCnpTechName = string.Empty;
                    WorkspaceView.CurrentCnpTechName = string.Empty;
                    return;
                }

                var crew = await _api.GetAsync<CurrentCrewDto>(
                    $"api/technicians/current-crew/{Uri.EscapeDataString(employeeId)}");

                _currentCnpTechName = string.IsNullOrWhiteSpace(crew?.DisplayText)
                    ? employeeId
                    : crew.DisplayText.Trim();

                WorkspaceView.CurrentCnpTechName = _currentCnpTechName;
            }
            catch
            {
                _currentCnpTechName = GetWindowsEmployeeId();
                WorkspaceView.CurrentCnpTechName = _currentCnpTechName;
            }
        }

        private static string GetWindowsEmployeeId()
        {
            var name = WindowsIdentity.GetCurrent()?.Name ?? string.Empty;

            if (name.Contains('\\'))
                name = name.Split('\\').Last();

            if (name.Contains('@'))
                name = name.Split('@').First();

            return name.Trim();
        }
    }
}