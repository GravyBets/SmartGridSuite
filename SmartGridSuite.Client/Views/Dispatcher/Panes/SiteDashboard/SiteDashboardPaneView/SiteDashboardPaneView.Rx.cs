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

                if (result is null || !result.Found || string.IsNullOrWhiteSpace(result.SiteId))
                {
                    WorkspaceView.ShowRxIpLookupResult(
                        null,
                        $"No associated site found for {ip}.");

                    TopBarView.StatusText = "No associated site found.";
                    return;
                }

                var message = result.MatchCount > 1
                    ? $"Found {result.MatchCount} possible matches. Showing the first match from {result.MatchSource}.{result.MatchField}."
                    : $"Found match from {result.MatchSource}.{result.MatchField}.";

                WorkspaceView.ShowRxIpLookupResult(result.SiteId, message);

                TopBarView.StatusText = $"Associated site found: {result.SiteId}.";
            }
            catch (Exception ex)
            {
                WorkspaceView.ShowRxIpLookupResult(null, $"Lookup failed: {ex.Message}");
                TopBarView.StatusText = $"RX IP lookup failed: {ex.Message}";
            }
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