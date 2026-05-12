using System.Net.NetworkInformation;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private void RefreshTopAccessPanel()
        {

            TopIpATextBox.Text = GetTopInfoValue("TOP IP A");
            TopIpBTextBox.Text = GetTopInfoValue("TOP IP B");

            TopIpAStateTextBlock.Text = string.Empty;
            TopIpBStateTextBlock.Text = string.Empty;


            OpenTopIpAButton.IsEnabled = !string.IsNullOrWhiteSpace(TopIpATextBox.Text);
            OpenTopIpBButton.IsEnabled = !string.IsNullOrWhiteSpace(TopIpBTextBox.Text);

            TestTopPairButton.IsEnabled =
                !string.IsNullOrWhiteSpace(TopIpATextBox.Text) &&
                !string.IsNullOrWhiteSpace(TopIpBTextBox.Text);
        }

        private static string GetShortTopAccessTitle(string fullTitle)
        {
            if (string.IsNullOrWhiteSpace(fullTitle))
                return "TOP Access";

            var text = fullTitle.Trim();

            var parenIndex = text.IndexOf(" (", StringComparison.Ordinal);

            if (parenIndex > 0)
                return text[..parenIndex].Trim();

            return text;
        }

        private string GetTopInfoValue(string label)
        {
            var lines = (TopInfoTextBox.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    continue;

                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                return line[(idx + 1)..].Trim();
            }

            return string.Empty;
        }

        private static void OpenTopIpInBrowser(string? ipText)
        {
            var ip = (ipText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ip) || ip == "—")
                return;

            var url = $"https://{ip}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void OpenTopIpAButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopIpInBrowser(TopIpATextBox.Text);
        }

        private void OpenTopIpBButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopIpInBrowser(TopIpBTextBox.Text);
        }

        private void OpenTopTunnelButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopTunnelRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void TestTopPairButton_Click(object sender, RoutedEventArgs e)
        {
            var ipA = TopIpATextBox.Text?.Trim();
            var ipB = TopIpBTextBox.Text?.Trim();

            ClearTopPairState();

            if (string.IsNullOrWhiteSpace(ipA) || string.IsNullOrWhiteSpace(ipB))
                return;

            TopIpAStateTextBlock.Text = "Testing...";
            TopIpBStateTextBlock.Text = "Testing...";

            var taskA = MeasureAveragePingMsAsync(ipA);
            var taskB = MeasureAveragePingMsAsync(ipB);

            await Task.WhenAll(taskA, taskB);

            var avgA = taskA.Result;
            var avgB = taskB.Result;

            ApplyTopPairState(avgA, avgB);
        }

        private static async Task<double?> MeasureAveragePingMsAsync(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            using var ping = new Ping();
            var successfulTimes = new List<long>();

            for (var i = 0; i < 3; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 500);
                    if (reply.Status == IPStatus.Success)
                        successfulTimes.Add(reply.RoundtripTime);
                }
                catch
                {
                }
            }

            if (successfulTimes.Count == 0)
                return null;

            return successfulTimes.Average();
        }

        private void ApplyTopPairState(double? avgA, double? avgB)
        {
            if (avgA is null && avgB is null)
            {
                TopIpAStateTextBlock.Text = "No Reply";
                TopIpBStateTextBlock.Text = "No Reply";
                return;
            }

            if (avgA is not null && avgB is null)
            {
                TopIpAStateTextBlock.Text = "Active";
                TopIpBStateTextBlock.Text = "Passive";
                return;
            }

            if (avgA is null && avgB is not null)
            {
                TopIpAStateTextBlock.Text = "Passive";
                TopIpBStateTextBlock.Text = "Active";
                return;
            }

            if (avgA <= avgB)
            {
                TopIpAStateTextBlock.Text = "Active";
                TopIpBStateTextBlock.Text = "Passive";
            }
            else
            {
                TopIpAStateTextBlock.Text = "Passive";
                TopIpBStateTextBlock.Text = "Active";
            }
        }

        private void ClearTopPairState()
        {
            TopIpAStateTextBlock.Text = string.Empty;
            TopIpBStateTextBlock.Text = string.Empty;
        }
    }
}