using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Tickets;
using System.Windows;
using static SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard.SiteDashboardWorkspaceView;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private async Task RefreshTicketInfoAsync(
            SiteDashboardTabSession session,
            CancellationToken ct,
            long preferredTicketId = 0)
        {
            var siteId =
                (session.HeaderText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                session.CurrentTicketId =
                    0;

                session.HasExplicitTicketContext =
                    false;

                session.TicketInfoText =
                    "No ticket data returned yet.";

                return;
            }

            /*
             * Only pass a TicketId to the resolver when this dashboard
             * session has deliberate/explicit ticket context.
             *
             * An inferred CurrentTicketId from an ordinary site search
             * must NEVER become authoritative just because Refresh was
             * clicked.
             */
            long? explicitTicketId =
                null;

            if (session.HasExplicitTicketContext)
            {
                if (preferredTicketId > 0)
                {
                    explicitTicketId =
                        preferredTicketId;
                }
                else if (session.CurrentTicketId > 0)
                {
                    explicitTicketId =
                        session.CurrentTicketId;
                }
            }

            var resolution =
                await _ticketsApi.ResolveSiteTicketAsync(
                    siteId,
                    GetWindowsEmployeeId(),
                    explicitTicketId,
                    ct);

            var resolutionType =
                (resolution.Resolution ?? string.Empty).Trim();

            if (resolutionType.Equals(
                    "NoActiveTicket",
                    StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * A manual/inferred dashboard session currently has no
                 * active ticket.
                 */
                session.CurrentTicketId =
                    0;

                session.HasExplicitTicketContext =
                    false;

                session.TicketInfoText =
                    string.IsNullOrWhiteSpace(resolution.Message)
                        ? "No active ticket is currently associated with this site."
                        : resolution.Message.Trim();

                return;
            }

            if (resolutionType.Equals(
                "ChoiceRequired",
                StringComparison.OrdinalIgnoreCase))
            {
                var selectedTicketId =
                    ShowTicketSelectionDialog(
                        resolution);

                if (!selectedTicketId.HasValue ||
                    selectedTicketId.Value <= 0)
                {
                    /*
                     * Technician cancelled the chooser.
                     * Leave the session unresolved rather than guessing.
                     */
                    session.CurrentTicketId =
                        0;

                    session.HasExplicitTicketContext =
                        false;

                    session.TicketInfoText =
                        string.IsNullOrWhiteSpace(resolution.Message)
                            ? "Multiple tickets require technician selection."
                            : resolution.Message.Trim()
                              + Environment.NewLine
                              + Environment.NewLine
                              + "No ticket was selected.";

                    return;
                }

                /*
                 * The technician deliberately selected an exact ticket.
                 * From this point forward it is authoritative for this tab.
                 */
                session.CurrentTicketId =
                    selectedTicketId.Value;

                session.HasExplicitTicketContext =
                    true;

                /*
                 * Run through the resolver one more time using explicit
                 * context. This validates the selected ticket/site pairing
                 * on the API and loads the exact live ticket record.
                 */
                await RefreshTicketInfoAsync(
                    session,
                    ct,
                    preferredTicketId: selectedTicketId.Value);

                return;
            }           

            if (!resolutionType.Equals(
                    "Resolved",
                    StringComparison.OrdinalIgnoreCase) ||
                !resolution.TicketId.HasValue ||
                resolution.TicketId.Value <= 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(resolution.Message)
                        ? "The API could not resolve the ticket for this site."
                        : resolution.Message.Trim());
            }

            var resolvedTicketId =
                resolution.TicketId.Value;

            /*
             * Load the exact resolved ticket instead of asking the client
             * to rank the site's tickets again.
             */
            var resolvedTicket =
                await _ticketsApi.GetTicketByIdAsync(
                    resolvedTicketId,
                    ct);

            if (resolvedTicket is null)
            {
                throw new InvalidOperationException(
                    $"Ticket {resolvedTicketId} was resolved but could not be loaded.");
            }

            session.CurrentTicketId =
                resolvedTicketId;

            /*
             * IMPORTANT:
             *
             * Do not set HasExplicitTicketContext = true here.
             *
             * If this was a manual dashboard search, the resolver merely
             * inferred the best current ticket. Submit must still resolve
             * again later in case another ticket was created while the
             * technician was working.
             *
             * If this session was already explicit, its flag remains true.
             */
            session.TicketInfoText =
                BuildTicketInfoSummaryFromTickets(
                    new[] { resolvedTicket });
        }

        private long? ShowTicketSelectionDialog(
            ResolveSiteTicketResponse resolution)
        {
            var candidates =
                resolution.Candidates ??
                new List<SiteTicketResolutionCandidateDto>();

            if (candidates.Count == 0)
                return null;

            var window =
                new SiteTicketSelectionWindow(
                    candidates,
                    resolution.Message)
                {
                    Owner =
                        Window.GetWindow(this)
                };

            var result =
                window.ShowDialog();

            if (result != true ||
                window.SelectedTicket is null ||
                window.SelectedTicket.TicketId <= 0)
            {
                return null;
            }

            return window.SelectedTicket.TicketId;
        }

        private string BuildTicketInfoSummaryFromTickets(IEnumerable<TicketListItemDto>? tickets)
        {
            var ticket = SelectBestTicket(tickets);
            if (ticket is null)
                return "No ticket data returned yet.";

            var lines = new List<string>();

            AddLine(lines, "Notification Name", GetObjectPropertyText(
                ticket,
                "NotificationName",
                "NotificationText",
                "NotificationDescription"));

            AddLine(lines, "Notification #", GetObjectPropertyText(
                ticket,
                "Notification",
                "NotificationNumber",
                "NotificationId"));

            AddLine(lines, "Problem/Issue", GetObjectPropertyText(
                ticket,
                "Problem",
                "Issue"));

            AddLine(lines, "Dispatch Notes", GetObjectPropertyText(
                ticket,
                "DispatchNotes",
                "DispatcherNotes",
                "DispatchNote",
                "DispatcherNote"));

            AddLine(lines, "Work Order", GetObjectPropertyText(
                ticket,
                "CurrentWorkOrder",
                "WorkOrder",
                "WorkOrderNumber"));

            AddLine(lines, "Work Order Type", NormalizeTicketWorkOrderType(GetObjectPropertyText(
                ticket,
                "WorkOrderClass",
                "WorkOrderType")));

            AddLine(lines, "Assigned To", GetObjectPropertyText(
                ticket,
                "AssignedTech",
                "Tech"));

            AddLine(lines, "Date Created", FormatTicketCreatedDate(GetObjectPropertyText(
                ticket,
                "CreatedAt",
                "Created")));

            AddLine(lines, "Current Status", GetObjectPropertyText(
                ticket,
                "Status",
                "TicketStatus"));

            return lines.Count == 0
                ? "No ticket data returned yet."
                : string.Join(Environment.NewLine, lines);
        }

        private TicketListItemDto? SelectBestTicket(IEnumerable<TicketListItemDto>? tickets)
        {
            return tickets?
                .Where(IsVisibleSiteDashboardTicket)
                .OrderByDescending(GetTicketStatusRank)
                .ThenByDescending(t => GetTicketDate(t, "LastActivityAt", "LastActivity"))
                .ThenByDescending(t => GetTicketDate(t, "CreatedAt", "Created"))
                .FirstOrDefault();
        }

        private static bool IsVisibleSiteDashboardTicket(TicketListItemDto ticket)
        {
            var status = (GetObjectPropertyText(ticket, "Status", "TicketStatus") ?? string.Empty).Trim();

            if (status.Equals("Closed", StringComparison.OrdinalIgnoreCase))
                return false;

            if (status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Canceled", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static int GetTicketStatusRank(TicketListItemDto ticket)
        {
            var status = (GetObjectPropertyText(ticket, "Status", "TicketStatus") ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(status))
                return 1;

            if (status.Equals("Assigned", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("In Progress", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Needs Review", StringComparison.OrdinalIgnoreCase))
                return 3;

            if (status.Equals("Completed, Awaiting Closure", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed Awaiting Closure", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return 2;

            if (status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Canceled", StringComparison.OrdinalIgnoreCase))
                return 0;

            return 1;
        }

        private static DateTime GetTicketDate(TicketListItemDto ticket, params string[] propertyNames)
        {
            var text = GetObjectPropertyText(ticket, propertyNames);
            return DateTime.TryParse(text, out var dt) ? dt : DateTime.MinValue;
        }

        private static string NormalizeTicketWorkOrderType(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.Equals(text, "Cap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Capital", StringComparison.OrdinalIgnoreCase))
                return "Capital";

            if (string.Equals(text, "Maint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Maintenance", StringComparison.OrdinalIgnoreCase))
                return "Maintenance";

            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        private static string FormatTicketCreatedDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            return DateTime.TryParse(value, out var dt)
                ? dt.ToString("MM-dd-yyyy")
                : value.Trim();
        }

        private async void WorkspaceView_RefreshTicketRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            try
            {
                TopBarView.StatusText = "Refreshing ticket...";

                /*
                 * Preserve the exact working-ticket context already attached
                 * to this Site Dashboard session.
                 */
                var currentTicketId =
                    session.CurrentTicketId;

                await RefreshTicketInfoAsync(
                    session,
                    CancellationToken.None,
                    preferredTicketId: currentTicketId);

                if (session.SessionKey == _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText = "Ticket refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Ticket refresh failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_TicketActionRequested(object? sender, TicketActionRequestedEventArgs e)
        {
            if (_ticketActionInProgress)
            {
                TopBarView.StatusText = "Ticket action already running...";
                return;
            }

            var session = GetSelectedSession();

            if (session is null)
                return;

            try
            {
                _ticketActionInProgress = true;

                switch (e.Action)
                {
                    case "RequestCapital":
                        await HandleRequestCapitalAsync(session, e);
                        break;

                    case "RequestMaintenance":
                        await HandleRequestMaintenanceAsync(session, e);
                        break;

                    case "RequestTicket":
                        await HandleRequestTicketAsync(session, e);
                        break;
                }
            }
            finally
            {
                _ticketActionInProgress = false;
            }
        }

        private async Task HandleRequestCapitalAsync(
            SiteDashboardTabSession session,
            TicketActionRequestedEventArgs e)
        {
            if (e.TicketId <= 0)
            {
                TopBarView.StatusText =
                    "No ticket is currently associated with this site.";

                return;
            }

            while (true)
            {
                try
                {
                    TopBarView.StatusText =
                        "Requesting Capital...";

                    await _ticketsApi.RequestCapitalAsync(
                        e.TicketId,
                        e.Reason,
                        requestedBy: Environment.UserName,
                        CancellationToken.None);

                    break;
                }
                catch (Exception ex)
                {
                    TopBarView.StatusText =
                        "Capital request could not be confirmed.";

                    var retry =
                        MessageBox.Show(
                            Window.GetWindow(this),
                            "SmartGridSuite could not confirm that the Capital request was received."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Check your network, VPN, and SmartGridSuite connection."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Your entered reason has been preserved. "
                            + "Would you like to try submitting the request again?"
                            + Environment.NewLine
                            + Environment.NewLine
                            + $"Details: {ex.Message}",
                            "Capital Request Could Not Be Confirmed",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                    if (retry != MessageBoxResult.Yes)
                        return;
                }
            }

            /*
             * Submission was confirmed.
             * Ticket refresh failure is not a submission failure.
             */
            try
            {
                /*
                 * The technician just deliberately performed an action
                 * against this exact ticket. That ticket is now explicit
                 * context for this dashboard session.
                 */
                session.CurrentTicketId =
                    e.TicketId;

                session.HasExplicitTicketContext =
                    true;

                await RefreshTicketInfoAsync(
                    session,
                    CancellationToken.None,
                    preferredTicketId: e.TicketId);

                if (session.SessionKey ==
                    _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText =
                    "Capital request saved.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText =
                    "Capital request saved, but ticket refresh failed.";

                MessageBox.Show(
                    Window.GetWindow(this),
                    "The Capital request was received, but SmartGridSuite "
                    + "could not refresh the Ticket area."
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Use Refresh Ticket after your connection is restored."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"Details: {ex.Message}",
                    "Ticket Refresh Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task HandleRequestMaintenanceAsync(
            SiteDashboardTabSession session,
            TicketActionRequestedEventArgs e)
        {
            if (e.TicketId <= 0)
            {
                TopBarView.StatusText =
                    "No ticket is currently associated with this site.";

                return;
            }

            while (true)
            {
                try
                {
                    TopBarView.StatusText =
                        "Requesting Maintenance...";

                    await _ticketsApi.RequestMaintenanceAsync(
                        e.TicketId,
                        e.Reason,
                        requestedBy: Environment.UserName,
                        CancellationToken.None);

                    break;
                }
                catch (Exception ex)
                {
                    TopBarView.StatusText =
                        "Maintenance request could not be confirmed.";

                    var retry =
                        MessageBox.Show(
                            Window.GetWindow(this),
                            "SmartGridSuite could not confirm that the Maintenance request was received."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Check your network, VPN, and SmartGridSuite connection."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Your entered reason has been preserved. "
                            + "Would you like to try submitting the request again?"
                            + Environment.NewLine
                            + Environment.NewLine
                            + $"Details: {ex.Message}",
                            "Maintenance Request Could Not Be Confirmed",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                    if (retry != MessageBoxResult.Yes)
                        return;
                }
            }

            /*
             * Submission was confirmed.
             * Treat the following dashboard refresh separately.
             */
            try
            {
                /*
                 * The technician just deliberately performed an action
                 * against this exact ticket. Preserve it as explicit
                 * dashboard context.
                 */
                session.CurrentTicketId =
                    e.TicketId;

                session.HasExplicitTicketContext =
                    true;

                await RefreshTicketInfoAsync(
                    session,
                    CancellationToken.None,
                    preferredTicketId: e.TicketId);

                if (session.SessionKey ==
                    _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText =
                    "Maintenance request saved.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText =
                    "Maintenance request saved, but ticket refresh failed.";

                MessageBox.Show(
                    Window.GetWindow(this),
                    "The Maintenance request was received, but SmartGridSuite "
                    + "could not refresh the Ticket area."
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Use Refresh Ticket after your connection is restored."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"Details: {ex.Message}",
                    "Ticket Refresh Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task HandleRequestTicketAsync(
            SiteDashboardTabSession session,
            TicketActionRequestedEventArgs e)
        {
            long newTicketId;

            /*
             * Keep retrying with the same entered reason if the technician
             * chooses Retry. RequestTicketAsync already protects against
             * creating a duplicate open Site Dashboard ticket request, so
             * retrying an uncertain connection is safe.
             */
            while (true)
            {
                try
                {
                    TopBarView.StatusText =
                        "Creating ticket request...";

                    newTicketId =
                        await _ticketsApi.RequestTicketAsync(
                            session.HeaderText,
                            e.Reason,
                            requestedBy: Environment.UserName,
                            CancellationToken.None);

                    if (newTicketId <= 0)
                    {
                        throw new InvalidOperationException(
                            "The API did not confirm a ticket ID.");
                    }

                    break;
                }
                catch (Exception ex)
                {
                    TopBarView.StatusText =
                        "Ticket request could not be confirmed.";

                    var retry =
                        MessageBox.Show(
                            Window.GetWindow(this),
                            "SmartGridSuite could not confirm that the Ticket request was received."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Check your network, VPN, and SmartGridSuite connection."
                            + Environment.NewLine
                            + Environment.NewLine
                            + "Your entered reason has been preserved. "
                            + "Would you like to try submitting the request again?"
                            + Environment.NewLine
                            + Environment.NewLine
                            + $"Details: {ex.Message}",
                            "Ticket Request Could Not Be Confirmed",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                    if (retry != MessageBoxResult.Yes)
                        return;
                }
            }

            /*
             * At this point the API has confirmed the request.
             * Refreshing the Ticket card is a separate operation so a
             * refresh problem is never reported as a failed submission.
             */
            /*
             * The technician deliberately created/requested this ticket,
             * so it becomes authoritative for this dashboard session.
             */
            session.CurrentTicketId =
                newTicketId;

            session.HasExplicitTicketContext =
                true;

            try
            {
                await RefreshTicketInfoAsync(
                    session,
                    CancellationToken.None,
                    preferredTicketId: newTicketId);

                if (session.SessionKey == _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText =
                    "Ticket request created.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText =
                    "Ticket request created, but ticket refresh failed.";

                MessageBox.Show(
                    Window.GetWindow(this),
                    "The Ticket request was received, but SmartGridSuite "
                    + "could not refresh the Ticket area."
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Use Refresh Ticket after your connection is restored."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"Details: {ex.Message}",
                    "Ticket Refresh Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}