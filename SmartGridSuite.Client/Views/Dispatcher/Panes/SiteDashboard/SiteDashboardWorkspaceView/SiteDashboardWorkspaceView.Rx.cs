using System.Diagnostics;
using System.Text.RegularExpressions;
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

        private static readonly Regex IpRegex =
                new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);

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

        private void SearchRxIpButton_Click(object sender, RoutedEventArgs e)
        {
            var ip = (RxIpLookupTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip))
            {
                RxIpLookupStatusTextBlock.Text = "Enter an IP address first.";
                RxAssociatedSiteTextBlock.Text = string.Empty;
                OpenAssociatedSiteButton.IsEnabled = false;
                _rxAssociatedSiteId = string.Empty;
                return;
            }

            if (!IpRegex.IsMatch(ip))
            {
                RxIpLookupStatusTextBlock.Text = "Enter a valid IPv4 address.";
                RxAssociatedSiteTextBlock.Text = string.Empty;
                OpenAssociatedSiteButton.IsEnabled = false;
                _rxAssociatedSiteId = string.Empty;
                return;
            }

            RxIpLookupStatusTextBlock.Text = "Searching...";
            RxAssociatedSiteTextBlock.Text = string.Empty;
            OpenAssociatedSiteButton.IsEnabled = false;
            _rxAssociatedSiteId = string.Empty;

            RxIpLookupRequested?.Invoke(this, ip);
        }

        private void OpenAssociatedSiteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_rxAssociatedSiteId))
                return;

            OpenAssociatedSiteRequested?.Invoke(this, _rxAssociatedSiteId);
        }

        public void ShowRxIpLookupResult(string? siteId, string? message = null)
        {
            var cleanSite = (siteId ?? string.Empty).Trim();

            _rxAssociatedSiteId = cleanSite;

            if (string.IsNullOrWhiteSpace(cleanSite))
            {
                RxIpLookupStatusTextBlock.Text = string.IsNullOrWhiteSpace(message)
                    ? "No associated site found for that IP."
                    : message.Trim();

                RxAssociatedSiteTextBlock.Text = string.Empty;
                OpenAssociatedSiteButton.IsEnabled = false;
                return;
            }

            RxIpLookupStatusTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Associated site found."
                : message.Trim();

            RxAssociatedSiteTextBlock.Text = cleanSite;
            OpenAssociatedSiteButton.IsEnabled = true;
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

            var value = button.Tag?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return;

            var copied = await TryCopyToClipboardAsync(value);

            if (!copied)
            {
                button.ToolTip = "Could not copy. Try again.";
                return;
            }

            var icon = FindVisualChildren<TextBlock>(button).FirstOrDefault();

            if (icon is null)
                return;

            icon.Text = CheckGlyph;
            button.ToolTip = "Copied!";

            await Task.Delay(TimeSpan.FromSeconds(3));

            icon.Text = CopyGlyph;
        }
    }
}