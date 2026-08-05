using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private string _rangeExtenderLinkUrl = string.Empty;

        public string RangeExtenderLinkUrl
        {
            get => _rangeExtenderLinkUrl;
            set => _rangeExtenderLinkUrl = value ?? string.Empty;
        }

        private string _rxAssociatedSiteId = string.Empty;

        private readonly List<RxAssociatedSiteLookupResult> _rxAssociatedSiteResults = new();

        public sealed class RxAssociatedSiteLookupResult
        {
            public string SiteId { get; set; } = "";
            public string DashboardKind { get; set; } = "";
            public string MatchSource { get; set; } = "";
            public string MatchField { get; set; } = "";

            public string DisplayText
            {
                get
                {
                    var siteId = string.IsNullOrWhiteSpace(SiteId)
                        ? "Unknown Site"
                        : SiteId.Trim();

                    var kind = string.IsNullOrWhiteSpace(DashboardKind)
                        ? "Unknown"
                        : DashboardKind.Trim();

                    var matchParts = new[]
                    {
                        MatchSource,
                        MatchField
                    }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList();

                    var matchText = matchParts.Count == 0
                        ? string.Empty
                        : $" — {string.Join(".", matchParts)}";

                    return $"{siteId}  ({kind}){matchText}";
                }
            }
        }

        private static readonly Regex IpRegex =
            new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
                RegexOptions.Compiled);

        private void OpenRangeExtenderLinkButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (RangeExtenderLinkUrl ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(
                    "Range Extender link URL has not been configured yet.",
                    "Range Extender",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open Range Extender link:{Environment.NewLine}{ex.Message}",
                    "Range Extender",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RxIpLookupTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;

            e.Handled = true;

            SearchRxIpButton_Click(
                sender,
                e);
        }

        private void SearchRxIpButton_Click(object sender, RoutedEventArgs e)
        {
            var query =
                (RxIpLookupTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                RxIpLookupStatusTextBlock.Text =
                    "Enter an IP address, PMR serial number, or RX serial number.";

                ClearRxAssociatedSiteResults();
                return;
            }

            RxIpLookupStatusTextBlock.Text = "Searching...";
            ClearRxAssociatedSiteResults();

            RxIpLookupRequested?.Invoke(this, query);
        }

        private void ClearRxAssociatedSiteResults()
        {
            _rxAssociatedSiteResults.Clear();
            _rxAssociatedSiteId = string.Empty;

            if (RxAssociatedSitesItemsControl is not null)
                RxAssociatedSitesItemsControl.Items.Refresh();
        }

        private void OpenAssociatedSiteResultButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var siteId = button.Tag?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return;

            _rxAssociatedSiteId = siteId;
            OpenAssociatedSiteRequested?.Invoke(this, siteId);
        }

        public void ShowRxIpLookupResults(
            IEnumerable<RxAssociatedSiteLookupResult>? results,
            string? message = null)
        {
            _rxAssociatedSiteResults.Clear();

            var orderedResults = (results ?? Enumerable.Empty<RxAssociatedSiteLookupResult>())
                .Where(x => !string.IsNullOrWhiteSpace(x.SiteId))
                .GroupBy(x => x.SiteId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(IsMrAssociatedSiteResult)
                .ThenBy(x => x.SiteId)
                .ToList();

            foreach (var result in orderedResults)
                _rxAssociatedSiteResults.Add(result);

            RxAssociatedSitesItemsControl.Items.Refresh();

            if (_rxAssociatedSiteResults.Count == 0)
            {
                _rxAssociatedSiteId = string.Empty;

                RxIpLookupStatusTextBlock.Text = string.IsNullOrWhiteSpace(message)
                    ? "No associated site found for that search value."
                    : message.Trim();

                return;
            }

            _rxAssociatedSiteId = _rxAssociatedSiteResults[0].SiteId;

            RxIpLookupStatusTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? $"Found {_rxAssociatedSiteResults.Count} associated site match(es)."
                : message.Trim();
        }

        public void ShowRxIpLookupResult(string? siteId, string? message = null)
        {
            var cleanSite = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanSite))
            {
                ShowRxIpLookupResults(
                    Array.Empty<RxAssociatedSiteLookupResult>(),
                    message);

                return;
            }

            ShowRxIpLookupResults(
                new[]
                {
                    new RxAssociatedSiteLookupResult
                    {
                        SiteId = cleanSite
                    }
                },
                message);
        }

        private static bool IsMrAssociatedSiteResult(RxAssociatedSiteLookupResult result)
        {
            var siteId = (result.SiteId ?? string.Empty).Trim();
            var kind = (result.DashboardKind ?? string.Empty).Trim();

            return kind.Equals("AMS/MR", StringComparison.OrdinalIgnoreCase) ||
                   kind.Equals("AmsMr", StringComparison.OrdinalIgnoreCase) ||
                   kind.Equals("MR", StringComparison.OrdinalIgnoreCase) ||
                   siteId.StartsWith("MR", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshRangeExtenderPanel()
        {
            if (RxRangeExtenderSnTextBox is null)
                return;

            RxRangeExtenderSnTextBox.Text = DashForDisplay(GetTopInfoValue("Range Extender SN"));
            RxMacAddressTextBox.Text = DashForDisplay(GetTopInfoValue("MAC Address"));
            RxPolePointTextBox.Text = DashForDisplay(GetTopInfoValue("Pole Point"));
            RxTransformerGlnTextBox.Text = DashForDisplay(GetTopInfoValue("Transformer GLN"));
        }

        private static string DashForDisplay(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private async void CopyRxReferenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var value = button.Tag?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return;

            var defaultToolTip = button.ToolTip?.ToString() ?? "Copy";

            var copied = await TryCopyToClipboardAsync(value);

            if (!copied)
            {
                button.ToolTip = "Could not copy. Try again.";
                return;
            }

            if (button.Content is not TextBlock glyphBlock)
                return;

            var originalText = glyphBlock.Text;

            glyphBlock.Text = CheckGlyph;
            button.ToolTip = "Copied!";

            await Task.Delay(TimeSpan.FromSeconds(3));

            glyphBlock.Text = string.IsNullOrWhiteSpace(originalText)
                ? CopyGlyph
                : originalText;

            button.ToolTip = defaultToolTip;
        }
    }
}