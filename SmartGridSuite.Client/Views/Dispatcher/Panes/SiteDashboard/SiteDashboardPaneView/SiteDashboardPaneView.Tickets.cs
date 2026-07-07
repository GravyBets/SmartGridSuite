using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Tickets;
using static SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard.SiteDashboardWorkspaceView;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private async Task RefreshTicketInfoAsync(SiteDashboardTabSession session, CancellationToken ct)
        {
            var siteId = session.HeaderText;
            if (string.IsNullOrWhiteSpace(siteId))
            {
                session.CurrentTicketId = 0;
                session.TicketInfoText = "No ticket data returned yet.";
                return;
            }

            var tickets = await _ticketsApi.GetTicketsBySiteAsync(siteId, ct);
            var bestTicket = SelectBestTicket(tickets);

            session.CurrentTicketId = bestTicket?.Id ?? 0;
            session.TicketInfoText = BuildTicketInfoSummaryFromTickets(
                bestTicket is null ? Array.Empty<TicketListItemDto>() : new[] { bestTicket });
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
                await RefreshTicketInfoAsync(session, CancellationToken.None);

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

        private async Task HandleRequestCapitalAsync(SiteDashboardTabSession session, TicketActionRequestedEventArgs e)
        {
            if (e.TicketId <= 0)
            {
                TopBarView.StatusText = "No ticket is currently associated with this site.";
                return;
            }

            try
            {
                TopBarView.StatusText = "Requesting Capital...";

                await _ticketsApi.RequestCapitalAsync(
                    e.TicketId,
                    e.Reason,
                    requestedBy: Environment.UserName,
                    CancellationToken.None);

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText = "Capital request saved.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Capital failed: {ex.Message}";
            }
        }

        private async Task HandleRequestMaintenanceAsync(SiteDashboardTabSession session, TicketActionRequestedEventArgs e)
        {
            if (e.TicketId <= 0)
            {
                TopBarView.StatusText = "No ticket is currently associated with this site.";
                return;
            }

            try
            {
                TopBarView.StatusText = "Requesting Maintenance...";

                await _ticketsApi.RequestMaintenanceAsync(
                    e.TicketId,
                    e.Reason,
                    requestedBy: Environment.UserName,
                    CancellationToken.None);

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText = "Maintenance request saved.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Maintenance failed: {ex.Message}";
            }
        }

        private async Task HandleRequestTicketAsync(SiteDashboardTabSession session, TicketActionRequestedEventArgs e)
        {
            try
            {
                TopBarView.StatusText = "Creating ticket request...";

                var newTicketId = await _ticketsApi.RequestTicketAsync(
                    session.HeaderText,
                    e.Reason,
                    requestedBy: Environment.UserName,
                    CancellationToken.None);

                session.CurrentTicketId = newTicketId;

                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                {
                    SaveCurrentTabUiState();
                    RenderSelectedSession();
                }

                TopBarView.StatusText = "Ticket request created.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Ticket failed: {ex.Message}";
            }
        }
    }
}