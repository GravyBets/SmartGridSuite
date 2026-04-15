using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardNetworkView : UserControl
    {
        private CancellationTokenSource? _primaryPingCts;
        private CancellationTokenSource? _lanPingCts;
        private CancellationTokenSource? _secondaryPingCts;

        public SiteDashboardNetworkView()
        {
            InitializeComponent();
            DataObject.AddPastingHandler(PingCountTextBox, PingCountTextBox_Pasting);
            Reset();
        }

        public string SiteHeader
        {
            get => NetworkHeaderTextBlock.Text;
            set => NetworkHeaderTextBlock.Text =
                string.IsNullOrWhiteSpace(value)
                    ? "Site"
                    : value.Trim();
        }

        public string PrimaryIp
        {
            get => PrimaryIpTextBox.Text;
            set => PrimaryIpTextBox.Text = NormalizeDisplay(value);
        }

        public string LanIp
        {
            get => LanIpTextBox.Text;
            set => LanIpTextBox.Text = NormalizeDisplay(value);
        }

        public string SecondaryIp
        {
            get => SecondaryIpTextBox.Text;
            set => SecondaryIpTextBox.Text = NormalizeDisplay(value);
        }

        public void Reset()
        {
            StopAllPings();

            PrimaryIp = string.Empty;
            LanIp = string.Empty;
            SecondaryIp = string.Empty;

            PingCountTextBox.Text = string.Empty;

            PrimarySummaryTextBlock.Text = "Ready.";
            LanSummaryTextBlock.Text = "Ready.";
            SecondarySummaryTextBlock.Text = "Ready.";

            PrimaryResultsTextBox.Text = string.Empty;
            LanResultsTextBox.Text = string.Empty;
            SecondaryResultsTextBox.Text = string.Empty;
            SiteHeader = string.Empty;

            ClearIpTestState(PrimaryIpTextBox);
            ClearIpTestState(LanIpTextBox);
            ClearIpTestState(SecondaryIpTextBox);
        }

        private async void PingPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAndDispose(ref _primaryPingCts);
            _primaryPingCts = new CancellationTokenSource();

            await PingTargetAsync(
                PrimaryIp,
                PrimarySummaryTextBlock,
                PrimaryResultsTextBox,
                _primaryPingCts.Token);
        }

        private async void PingLanButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAndDispose(ref _lanPingCts);
            _lanPingCts = new CancellationTokenSource();

            await PingTargetAsync(
                LanIp,
                LanSummaryTextBlock,
                LanResultsTextBox,
                _lanPingCts.Token);
        }

        private async void PingSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAndDispose(ref _secondaryPingCts);
            _secondaryPingCts = new CancellationTokenSource();

            await PingTargetAsync(
                SecondaryIp,
                SecondarySummaryTextBlock,
                SecondaryResultsTextBox,
                _secondaryPingCts.Token);
        }

        private async void PingAllButton_Click(object sender, RoutedEventArgs e)
        {
            StopAllPings();

            _primaryPingCts = new CancellationTokenSource();
            _lanPingCts = new CancellationTokenSource();
            _secondaryPingCts = new CancellationTokenSource();

            var primaryTask = PingTargetAsync(
                PrimaryIp,
                PrimarySummaryTextBlock,
                PrimaryResultsTextBox,
                _primaryPingCts.Token);

            var lanTask = PingTargetAsync(
                LanIp,
                LanSummaryTextBlock,
                LanResultsTextBox,
                _lanPingCts.Token);

            var secondaryTask = PingTargetAsync(
                SecondaryIp,
                SecondarySummaryTextBlock,
                SecondaryResultsTextBox,
                _secondaryPingCts.Token);

            await Task.WhenAll(primaryTask, lanTask, secondaryTask);
        }

        private void StopAllButton_Click(object sender, RoutedEventArgs e)
        {
            StopAllPings();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            PrimaryResultsTextBox.Clear();
            LanResultsTextBox.Clear();
            SecondaryResultsTextBox.Clear();

            PrimarySummaryTextBlock.Text = "Ready.";
            LanSummaryTextBlock.Text = "Ready.";
            SecondarySummaryTextBlock.Text = "Ready.";

            ClearIpTestState(PrimaryIpTextBox);
            ClearIpTestState(LanIpTextBox);
            ClearIpTestState(SecondaryIpTextBox);
        }

        private static void ClearIpTestState(TextBox ipTextBox)
        {
            ipTextBox.ClearValue(Control.BackgroundProperty);
            ipTextBox.ClearValue(Control.BorderBrushProperty);
        }

        private async Task PingTargetAsync(string ipText, TextBlock summaryTextBlock, 
            TextBox resultsTextBox, CancellationToken cancellationToken)
        {
            resultsTextBox.Text = string.Empty;

            var ip = (ipText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ip) || ip == "—")
            {
                summaryTextBlock.Text = "No IP available.";
                return;
            }

            var requestedCount = ParsePingCount();
            var sent = 0;
            var lost = 0;

            using var ping = new Ping();

            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       (requestedCount is null || sent < requestedCount.Value))
                {
                    PingReply? reply = null;
                    string line;

                    try
                    {
                        reply = await ping.SendPingAsync(ip, 1500);
                        sent++;

                        if (reply.Status == IPStatus.Success)
                        {
                            line = $"{DateTime.Now:HH:mm:ss} {ip}: Time={reply.RoundtripTime} ms";
                        }
                        else
                        {
                            lost++;
                            line = $"{DateTime.Now:HH:mm:ss} {ip}: {reply.Status}";
                        }
                    }
                    catch (Exception ex)
                    {
                        sent++;
                        lost++;
                        line = $"{DateTime.Now:HH:mm:ss} {ip}: {ex.Message}";
                    }

                    AppendResult(resultsTextBox, line);

                    var lossPercent = sent == 0 ? 0 : (int)Math.Round((double)lost * 100 / sent);
                    summaryTextBlock.Text = $"Sent = {sent}, Lost = {lost} ({lossPercent}% loss).";

                    if (requestedCount is null)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (sent == 0)
                    summaryTextBlock.Text = "Stopped.";
            }
        }

        private int? ParsePingCount()
        {
            var raw = PingCountTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (!int.TryParse(raw, out var count))
                return 3;

            if (count <= 0 || count >= 999999)
                return 3;

            return count;
        }

        //Ping Helpers
        private void PingCountTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            e.Handled = !IsValidNextPingCountText(textBox, e.Text);
        }

        private void PingCountTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (!e.DataObject.GetDataPresent(typeof(string)))
            {
                e.CancelCommand();
                return;
            }

            var pastedText = e.DataObject.GetData(typeof(string)) as string ?? string.Empty;

            if (!IsValidNextPingCountText(textBox, pastedText))
                e.CancelCommand();
        }

        private static bool IsValidNextPingCountText(TextBox textBox, string incomingText)
        {
            var current = textBox.Text ?? string.Empty;

            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;

            var nextText = current.Remove(selectionStart, selectionLength)
                                  .Insert(selectionStart, incomingText);

            if (string.IsNullOrWhiteSpace(nextText))
                return true;

            if (!nextText.All(char.IsDigit))
                return false;

            if (!int.TryParse(nextText, out var value))
                return false;

            return value < 999999;
        }

        private void StopAllPings()
        {
            CancelAndDispose(ref _primaryPingCts);
            CancelAndDispose(ref _lanPingCts);
            CancelAndDispose(ref _secondaryPingCts);
        }



        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts is null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
            cts = null;
        }

        private static string NormalizeDisplay(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static void AppendResult(TextBox textBox, string line)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
                textBox.Text = line;
            else
                textBox.AppendText(Environment.NewLine + line);

            textBox.ScrollToEnd();
        }


        //Methods for IPs to go Green/Red
        private const int QuickTestTimeoutMs = 450;
        private const int QuickTestWarmupCount = 1;
        private const int QuickTestMeasuredCount = 4;

        private async Task RunQuickReachabilityTestAsync(TextBox ipTextBox, TextBlock summaryTextBlock)
        {
            var ip = ipTextBox.Text?.Trim();

            ClearIpTestState(ipTextBox);
            summaryTextBlock.Text = "Testing...";

            if (string.IsNullOrWhiteSpace(ip))
            {
                summaryTextBlock.Text = "Test Failed";
                return;
            }

            using var ping = new Ping();

            var successCount = 0;

            for (var i = 0; i < QuickTestWarmupCount + QuickTestMeasuredCount; i++)
            {
                var success = false;

                try
                {
                    var reply = await ping.SendPingAsync(ip, QuickTestTimeoutMs);
                    success = reply.Status == IPStatus.Success;
                }
                catch
                {
                    success = false;
                }

                // Ignore first ping
                if (i < QuickTestWarmupCount)
                    continue;

                if (success)
                    successCount++;
            }

            var isSuccessful = successCount > 0;

            SetIpTestState(ipTextBox, isSuccessful);
            summaryTextBlock.Text = isSuccessful ? "Test Successful" : "Test Failed";
        }

        private static void SetIpTestState(TextBox ipTextBox, bool? isSuccessful)
        {
            if (isSuccessful is null)
            {
                ClearIpTestState(ipTextBox);
                return;
            }

            Color background;
            Color border;

            if (isSuccessful.Value)
            {
                background = Color.FromRgb(232, 245, 233);   // green
                border = Color.FromRgb(76, 175, 80);
            }
            else
            {
                background = Color.FromRgb(253, 236, 234);   // red
                border = Color.FromRgb(220, 80, 80);
            }

            ipTextBox.Background = new SolidColorBrush(background);
            ipTextBox.BorderBrush = new SolidColorBrush(border);
        }




        public Task RunQuickReachabilityTestForAllAsync()
        {
            return Task.WhenAll(
                RunQuickReachabilityTestAsync(PrimaryIpTextBox, PrimarySummaryTextBlock),
                RunQuickReachabilityTestAsync(LanIpTextBox, LanSummaryTextBlock),
                RunQuickReachabilityTestAsync(SecondaryIpTextBox, SecondarySummaryTextBlock));
        }

        private async void TestAllButton_Click(object sender, RoutedEventArgs e)
        {
            await RunQuickReachabilityTestForAllAsync();
        }


    }
}