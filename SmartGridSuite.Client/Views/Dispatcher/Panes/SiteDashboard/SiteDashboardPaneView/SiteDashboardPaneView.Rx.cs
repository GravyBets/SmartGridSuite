using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.SiteDashboard;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private async void WorkspaceView_RxIpLookupRequested(object? sender, string ip)
        {
            try
            {
                TopBarView.StatusText = $"Looking up associated site for {ip}...";

                var result = await _api.GetAsync<AssociatedSiteByIpLookupDto>(
                    $"api/site-dashboard/associated-site-by-ip?ip={Uri.EscapeDataString(ip)}",
                    CancellationToken.None);

                if (result is null || !result.Found)
                {
                    WorkspaceView.ShowRxIpLookupResults(
                        Array.Empty<SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult>(),
                        $"No associated site found for {ip}.");

                    TopBarView.StatusText = "No associated site found.";
                    return;
                }

                var matches = BuildRxAssociatedSiteLookupResults(result)
                    .OrderByDescending(SiteDashboardWorkspaceView_IsMrLookupResult)
                    .ThenBy(x => x.SiteId)
                    .ToList();

                if (matches.Count == 0)
                {
                    WorkspaceView.ShowRxIpLookupResults(
                        Array.Empty<SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult>(),
                        $"No associated site found for {ip}.");

                    TopBarView.StatusText = "No associated site found.";
                    return;
                }

                var message = matches.Count == 1
                    ? $"Found 1 associated site."
                    : $"Found {matches.Count} possible matches. MR sites are listed first.";

                WorkspaceView.ShowRxIpLookupResults(matches, message);

                TopBarView.StatusText = matches.Count == 1
                    ? $"Associated site found: {matches[0].SiteId}."
                    : $"Found {matches.Count} associated site matches.";
            }
            catch (Exception ex)
            {
                WorkspaceView.ShowRxIpLookupResults(
                    Array.Empty<SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult>(),
                    $"Lookup failed: {ex.Message}");

                TopBarView.StatusText = $"RX IP lookup failed: {ex.Message}";
            }
        }

        private static List<SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult> BuildRxAssociatedSiteLookupResults(
            AssociatedSiteByIpLookupDto result)
        {
            var matches = new List<SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult>();

            /*
             * This supports the current single-result DTO and the future all-match DTO.
             * If your Contracts DTO already has Matches, use that block.
             */

            var matchesProperty = result.GetType().GetProperty("Matches");
            var rawMatches = matchesProperty?.GetValue(result) as System.Collections.IEnumerable;

            if (rawMatches is not null)
            {
                foreach (var rawMatch in rawMatches)
                {
                    var siteId = GetObjectPropertyText(rawMatch, "SiteId", "Site", "TopName");
                    if (string.IsNullOrWhiteSpace(siteId))
                        continue;

                    matches.Add(new SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult
                    {
                        SiteId = siteId.Trim(),
                        DashboardKind = GetObjectPropertyText(rawMatch, "DashboardKind", "Kind", "SiteKind") ?? string.Empty,
                        MatchSource = GetObjectPropertyText(rawMatch, "MatchSource", "Source") ?? string.Empty,
                        MatchField = GetObjectPropertyText(rawMatch, "MatchField", "Field") ?? string.Empty
                    });
                }
            }

            if (matches.Count == 0 && !string.IsNullOrWhiteSpace(result.SiteId))
            {
                matches.Add(new SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult
                {
                    SiteId = result.SiteId.Trim(),
                    MatchSource = result.MatchSource ?? string.Empty,
                    MatchField = result.MatchField ?? string.Empty
                });
            }

            return matches
                .GroupBy(x => x.SiteId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static bool SiteDashboardWorkspaceView_IsMrLookupResult(
            SiteDashboardWorkspaceView.RxAssociatedSiteLookupResult result)
        {
            var siteId = (result.SiteId ?? string.Empty).Trim();
            var kind = (result.DashboardKind ?? string.Empty).Trim();

            return kind.Equals("AMS/MR", StringComparison.OrdinalIgnoreCase) ||
                   kind.Equals("AmsMr", StringComparison.OrdinalIgnoreCase) ||
                   kind.Equals("MR", StringComparison.OrdinalIgnoreCase) ||
                   siteId.StartsWith("MR", StringComparison.OrdinalIgnoreCase);
        }

        private async void WorkspaceView_OpenAssociatedSiteRequested(object? sender, string siteId)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return;

            var existingSession = _sessions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.HeaderText, siteId, StringComparison.OrdinalIgnoreCase));

            if (existingSession is not null)
            {
                SaveCurrentTabUiState();

                _selectedSessionKey = existingSession.SessionKey;
                RenderSelectedSession();
                TopBarView.StatusText = $"Switched to {siteId}.";
                return;
            }

            SaveCurrentTabUiState();

            CreateBlankTab(selectNewTab: true);
            RenderSelectedSession();

            await LoadAsync(siteId);
        }
    }
}