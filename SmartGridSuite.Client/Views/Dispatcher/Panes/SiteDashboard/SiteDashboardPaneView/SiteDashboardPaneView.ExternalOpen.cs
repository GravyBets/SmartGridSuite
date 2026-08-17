using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.FieldTechnician;
using System.Threading;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        public async Task OpenTicketsFromFieldTechTasksAsync(
            IEnumerable<FieldTechTicketListItemDto> tickets)
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

            /*
             * Capture the currently visible dashboard BEFORE changing
             * which session is selected.
             *
             * This is especially important when the technician has an
             * unsubmitted write-up on the current site.
             */
            SaveCurrentTabUiState();

            foreach (var ticket in cleanTickets)
            {
                var site =
                    (ticket.Site ?? string.Empty).Trim();

                if (!IsSiteAlreadyOpen(site) &&
                    !CanLoadIntoSelectedTab())
                {
                    /*
                     * Do not merely create/select a blank session.
                     *
                     * WorkspaceView is shared by every dashboard tab. Until
                     * RenderSelectedSession runs, it still visually contains
                     * the PREVIOUS site's write-up.
                     *
                     * LoadAsync begins by saving the visible WorkspaceView
                     * into the selected session. If we skip this render, the
                     * previous site's write-up is accidentally copied into
                     * the new Blank session.
                     */
                    CreateBlankTab(
                        selectNewTab: true);

                    RenderSelectedSession();

                    /*
                     * Allow WPF bindings/layout to finish switching the
                     * shared WorkspaceView to the new blank session before
                     * LoadAsync captures any UI state.
                     *
                     * This matches the existing Open All workflow.
                     */
                    await Dispatcher.InvokeAsync(
                        () => { },
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }

                /*
                 * Load the site normally first. This loads all Site Dashboard
                 * information and performs the normal site ticket lookup.
                 *
                 * LoadAsync may also return without loading when the user
                 * declines an unsaved-write-up warning. Therefore we MUST
                 * verify the selected session afterward before applying
                 * explicit ticket context.
                 */
                await LoadAsync(site);

                var session =
                    GetSelectedSession();

                if (session is null)
                    continue;

                /*
                 * A cancelled or failed site load must never fall through and
                 * attach the task's TicketId to whatever session happens to
                 * still be selected.
                 *
                 * This is also what prevents a "No" response to an unsaved
                 * write-up warning from continuing against a Blank tab.
                 */
                if (!SessionMatchesSite(
                        session,
                        site))
                {
                    continue;
                }

                /*
                 * My Tasks knows exactly which ticket the technician opened.
                 * Make that ticket authoritative for this dashboard session
                 * instead of allowing the site-level ticket search to guess.
                 */
                session.CurrentTicketId =
                    ticket.Id;

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
            var candidates =
                BuildSiteDashboardSearchCandidates(site)
                    .ToList();

            return _sessions.Any(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase) &&
                candidates.Any(candidate =>
                    string.Equals(
                        x.HeaderText,
                        candidate,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        x.SearchText,
                        candidate,
                        StringComparison.OrdinalIgnoreCase)));
        }

        private bool CanLoadIntoSelectedTab()
        {
            var selectedSession =
                GetSelectedSession();

            if (selectedSession is null)
                return false;

            var currentSearchText =
                (selectedSession.SearchText ?? string.Empty)
                    .Trim();

            var currentHeaderText =
                (selectedSession.HeaderText ?? string.Empty)
                    .Trim();

            /*
             * A session is reusable as a Blank tab only when BOTH pieces
             * of identifying state say it is blank.
             *
             * Previously this used OR, which meant a legitimate loaded
             * site could be treated as Blank if only one property had not
             * been populated yet.
             */
            var searchLooksBlank =
                string.IsNullOrWhiteSpace(
                    currentSearchText) ||
                currentSearchText.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase);

            var headerLooksBlank =
                string.IsNullOrWhiteSpace(
                    currentHeaderText) ||
                currentHeaderText.StartsWith(
                    "Blank",
                    StringComparison.OrdinalIgnoreCase);

            return searchLooksBlank &&
                   headerLooksBlank;
        }

        private static bool SessionMatchesSite(
            SiteDashboardTabSession session,
            string site)
        {
            var candidates =
                BuildSiteDashboardSearchCandidates(site)
                    .ToList();

            if (candidates.Count == 0)
                return false;

            var header =
                (session.HeaderText ?? string.Empty)
                    .Trim();

            var search =
                (session.SearchText ?? string.Empty)
                    .Trim();

            return candidates.Any(candidate =>
                string.Equals(
                    header,
                    candidate,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    search,
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}