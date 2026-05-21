using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        public async Task OpenSitesFromFieldTechAsync(IEnumerable<string> sites)
        {
            var cleanSites = sites
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanSites.Count == 0)
                return;

            SaveCurrentTabUiState();

            foreach (var site in cleanSites)
            {
                if (!IsSiteAlreadyOpen(site) && !CanLoadIntoSelectedTab())
                {
                    CreateBlankTab(selectNewTab: true);
                }

                await LoadAsync(site);
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