using SmartGridSuite.Contracts.FieldTechnician;
using System.Threading;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        public async Task OpenTicketsFromFieldTechTasksAsync(IEnumerable<FieldTechTicketListItemDto> tickets)
        {
            var cleanTickets = tickets
                .Where(x =>
                    x != null &&
                    x.Id > 0 &&
                    !string.IsNullOrWhiteSpace(x.Site))
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList();

            if (cleanTickets.Count == 0)
                return;

            SaveCurrentTabUiState();

            foreach (var ticket in cleanTickets)
            {
                var site =
                    (ticket.Site ?? string.Empty).Trim();

                if (!IsSiteAlreadyOpen(site) &&
                    !CanLoadIntoSelectedTab())
                {
                    CreateBlankTab(
                        selectNewTab: true);
                }

                /*
                 * Load the site normally first. This loads all Site Dashboard
                 * information and performs the normal site ticket lookup.
                 */
                await LoadAsync(site);

                /*
                 * My Tasks knows exactly which ticket the technician opened.
                 * Make that ticket authoritative for this dashboard session
                 * instead of allowing the site-level ticket search to guess.
                 */
                var session =
                    GetSelectedSession();

                if (session is null)
                    continue;

                session.CurrentTicketId = ticket.Id;

                session.HasExplicitTicketContext =
                    true;

                await RefreshTicketInfoAsync(
                    session,
                    CancellationToken.None,
                    preferredTicketId: ticket.Id);

                if (session.SessionKey ==
                    _selectedSessionKey)
                {
                    RenderSelectedSession();
                }
            }
        }

        private bool IsSiteAlreadyOpen(string site)
        {
            var candidates = BuildSiteDashboardSearchCandidates(site).ToList();

            return _sessions.Any(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                candidates.Any(candidate =>
                    string.Equals(x.HeaderText, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.SearchText, candidate, StringComparison.OrdinalIgnoreCase)));
        }

        private bool CanLoadIntoSelectedTab()
        {
            var selectedSession = GetSelectedSession();

            if (selectedSession is null)
                return false;

            var currentSearchText = (selectedSession.SearchText ?? string.Empty).Trim();
            var currentHeaderText = (selectedSession.HeaderText ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(currentSearchText) ||
                   currentSearchText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) ||
                   currentHeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase);
        }
    }
}