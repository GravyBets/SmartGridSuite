using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Linq;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardNetworkView : UserControl
    {
        private CancellationTokenSource? _primaryPingCts;
        private CancellationTokenSource? _lanPingCts;
        private CancellationTokenSource? _secondaryPingCts;

        private bool? _primaryTestSuccessful;
        private bool? _lanTestSuccessful;
        private bool? _secondaryTestSuccessful;

        public bool IsIgsdMode { get; set; }

        public SiteDashboardNetworkView()
        {
            InitializeComponent();
            DataObject.AddPastingHandler(PingCountTextBox, PingCountTextBox_Pasting);
            Reset();
        }

        //Layout Helper
        public void ApplyLayoutMode()
        {
            if (IsIgsdMode)
            {
                LanSectionBorder.Visibility = Visibility.Collapsed;
                PrimaryRtuReferenceSectionBorder.Visibility = Visibility.Visible;
                SecondaryReferenceSectionBorder.Visibility = Visibility.Visible;

                PrimarySectionRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleSectionRow.Height = GridLength.Auto;
                SecondarySectionRow.Height = new GridLength(1, GridUnitType.Star);
                BottomReferenceRow.Height = GridLength.Auto;
            }
            else
            {
                LanSectionBorder.Visibility = Visibility.Visible;
                PrimaryRtuReferenceSectionBorder.Visibility = Visibility.Collapsed;
                SecondaryReferenceSectionBorder.Visibility = Visibility.Collapsed;

                PrimarySectionRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleSectionRow.Height = new GridLength(1, GridUnitType.Star);
                SecondarySectionRow.Height = new GridLength(1, GridUnitType.Star);
                BottomReferenceRow.Height = new GridLength(0);
            }
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

        public string PrimaryPingLabel
        {
            get => PrimaryPingLabelTextBlock.Text;
            set => PrimaryPingLabelTextBlock.Text = CleanNetworkLabel(value, "Primary");
        }

        public string LanPingLabel
        {
            get => LanPingLabelTextBlock.Text;
            set => LanPingLabelTextBlock.Text = CleanNetworkLabel(value, "LAN");
        }

        public string SecondaryPingLabel
        {
            get => SecondaryPingLabelTextBlock.Text;
            set => SecondaryPingLabelTextBlock.Text = CleanNetworkLabel(value, "Secondary");
        }

        private static string CleanNetworkLabel(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }

        public void Reset()
        {
            StopAllPings();

            IsIgsdMode = false;
            PrimaryPingLabel = "Primary";
            LanPingLabel = "LAN";
            SecondaryPingLabel = "Secondary";


            PrimaryIp = string.Empty;
            LanIp = string.Empty;
            SecondaryIp = string.Empty;

            IgsdPrimaryRtuIp = string.Empty;
            IgsdPrimaryCommsEthernetIp = string.Empty;
            IgsdSecondaryCommsEthernetIp = string.Empty;
            IgsdSecondaryRtuIp = string.Empty;

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

            _primaryTestSuccessful = null;
            _lanTestSuccessful = null;
            _secondaryTestSuccessful = null;

            ApplyLayoutMode();
        }

        public NetworkPingSessionState GetPingSessionState()
        {
            return new NetworkPingSessionState
            {
                PingCount = PingCountTextBox.Text ?? string.Empty,

                Primary = new NetworkPingTargetState
                {
                    Ip = SnapshotIp(PrimaryIpTextBox.Text),
                    Results = PrimaryResultsTextBox.Text ?? string.Empty,
                    Summary = PrimarySummaryTextBlock.Text ?? "Ready.",
                    TestSuccessful = _primaryTestSuccessful
                },

                Lan = new NetworkPingTargetState
                {
                    Ip = SnapshotIp(LanIpTextBox.Text),
                    Results = LanResultsTextBox.Text ?? string.Empty,
                    Summary = LanSummaryTextBlock.Text ?? "Ready.",
                    TestSuccessful = _lanTestSuccessful
                },

                Secondary = new NetworkPingTargetState
                {
                    Ip = SnapshotIp(SecondaryIpTextBox.Text),
                    Results = SecondaryResultsTextBox.Text ?? string.Empty,
                    Summary = SecondarySummaryTextBlock.Text ?? "Ready.",
                    TestSuccessful = _secondaryTestSuccessful
                },

                IgsdPrimaryRtuIp = IgsdPrimaryRtuIpTextBox.Text ?? string.Empty,
                IgsdPrimaryCommsEthernetIp = IgsdPrimaryCommsEthernetIpTextBox.Text ?? string.Empty,
                IgsdSecondaryCommsEthernetIp = IgsdSecondaryCommsEthernetIpTextBox.Text ?? string.Empty,
                IgsdSecondaryRtuIp = IgsdSecondaryRtuIpTextBox.Text ?? string.Empty
            };
        }

        public void RestorePingSessionState(NetworkPingSessionState? state)
        {
            if (state is null)
                return;

            PingCountTextBox.Text = state.PingCount ?? string.Empty;

            RestorePingTargetState(
                state.Primary,
                PrimaryIpTextBox,
                PrimaryResultsTextBox,
                PrimarySummaryTextBlock,
                ref _primaryTestSuccessful);

            RestorePingTargetState(
                state.Lan,
                LanIpTextBox,
                LanResultsTextBox,
                LanSummaryTextBlock,
                ref _lanTestSuccessful);

            RestorePingTargetState(
                state.Secondary,
                SecondaryIpTextBox,
                SecondaryResultsTextBox,
                SecondarySummaryTextBlock,
                ref _secondaryTestSuccessful);

            if (!string.IsNullOrWhiteSpace(state.IgsdPrimaryRtuIp))
                IgsdPrimaryRtuIpTextBox.Text = state.IgsdPrimaryRtuIp;

            if (!string.IsNullOrWhiteSpace(state.IgsdPrimaryCommsEthernetIp))
                IgsdPrimaryCommsEthernetIpTextBox.Text = state.IgsdPrimaryCommsEthernetIp;

            if (!string.IsNullOrWhiteSpace(state.IgsdSecondaryCommsEthernetIp))
                IgsdSecondaryCommsEthernetIpTextBox.Text = state.IgsdSecondaryCommsEthernetIp;

            if (!string.IsNullOrWhiteSpace(state.IgsdSecondaryRtuIp))
                IgsdSecondaryRtuIpTextBox.Text = state.IgsdSecondaryRtuIp;
        }

        private static void RestorePingTargetState(NetworkPingTargetState? state, TextBox ipTextBox, TextBox resultsTextBox,
            TextBlock summaryTextBlock, ref bool? testStateField)
        {
            if (state is null)
                return;

            if (IsUsableSnapshotIp(state.Ip))
                ipTextBox.Text = state.Ip.Trim();

            resultsTextBox.Text = state.Results ?? string.Empty;
            summaryTextBlock.Text = string.IsNullOrWhiteSpace(state.Summary)
                ? "Ready."
                : state.Summary;

            testStateField = state.TestSuccessful;
            SetIpTestState(ipTextBox, state.TestSuccessful);
        }

        private static bool IsUsableSnapshotIp(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            return !string.IsNullOrWhiteSpace(text) &&
                   text != "—";
        }

        private static string SnapshotIp(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(text) || text == "—"
                ? string.Empty
                : text;
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
            _secondaryPingCts = new CancellationTokenSource();

            var tasks = new List<Task>
            {
                PingTargetAsync(
                    PrimaryIp,
                    PrimarySummaryTextBlock,
                    PrimaryResultsTextBox,
                    _primaryPingCts.Token),

                PingTargetAsync(
                    SecondaryIp,
                    SecondarySummaryTextBlock,
                    SecondaryResultsTextBox,
                    _secondaryPingCts.Token)
            };

            if (!IsIgsdMode)
            {
                _lanPingCts = new CancellationTokenSource();

                tasks.Insert(1, PingTargetAsync(
                    LanIp,
                    LanSummaryTextBlock,
                    LanResultsTextBox,
                    _lanPingCts.Token));
            }

            await Task.WhenAll(tasks);
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

            _primaryTestSuccessful = null;
            _lanTestSuccessful = null;
            _secondaryTestSuccessful = null;
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

        //IGSD View Mode
        public string IgsdPrimaryRtuIp
        {
            get => IgsdPrimaryRtuIpTextBox.Text;
            set => IgsdPrimaryRtuIpTextBox.Text = NormalizeDisplay(value);
        }

        public string IgsdPrimaryCommsEthernetIp
        {
            get => IgsdPrimaryCommsEthernetIpTextBox.Text;
            set
            {
                var normalized = NormalizeDisplay(value);
                var hasValue = !string.IsNullOrWhiteSpace(normalized) && normalized != "—";

                IgsdPrimaryCommsEthernetIpTextBox.Text = hasValue ? normalized : string.Empty;
                PrimaryCommsEthRow.Visibility = hasValue
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public string IgsdSecondaryCommsEthernetIp
        {
            get => IgsdSecondaryCommsEthernetIpTextBox.Text;
            set => IgsdSecondaryCommsEthernetIpTextBox.Text = NormalizeDisplay(value);
        }

        public string IgsdSecondaryRtuIp
        {
            get => IgsdSecondaryRtuIpTextBox.Text;
            set => IgsdSecondaryRtuIpTextBox.Text = NormalizeDisplay(value);
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
            RememberIpTestState(ipTextBox, null);
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
            RememberIpTestState(ipTextBox, isSuccessful);
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

        private void RememberIpTestState(TextBox ipTextBox, bool? isSuccessful)
        {
            if (ReferenceEquals(ipTextBox, PrimaryIpTextBox))
            {
                _primaryTestSuccessful = isSuccessful;
                return;
            }

            if (ReferenceEquals(ipTextBox, LanIpTextBox))
            {
                _lanTestSuccessful = isSuccessful;
                return;
            }

            if (ReferenceEquals(ipTextBox, SecondaryIpTextBox))
            {
                _secondaryTestSuccessful = isSuccessful;
            }
        }

        public string GetPingStatsForWriteUp()
        {
            var lines = new List<string>
            {
                "Ping Stats:"
            };

            var hasAnyPingStats = false;

            if (TryAddPingWriteUpBlock(
                    lines,
                    PrimaryPingLabel,
                    PrimaryIpTextBox.Text,
                    PrimarySummaryTextBlock.Text))
            {
                hasAnyPingStats = true;

                AddReferenceIpLine(lines, "Primary Comms Eth IP", IgsdPrimaryCommsEthernetIpTextBox.Text);
                AddReferenceIpLine(lines, "Primary RTU IP", IgsdPrimaryRtuIpTextBox.Text);
            }

            if (!IsIgsdMode)
            {
                if (TryAddPingWriteUpBlock(
                        lines,
                        LanPingLabel,
                        LanIpTextBox.Text,
                        LanSummaryTextBlock.Text))
                {
                    hasAnyPingStats = true;
                }
            }

            if (TryAddPingWriteUpBlock(
                    lines,
                    SecondaryPingLabel,
                    SecondaryIpTextBox.Text,
                    SecondarySummaryTextBlock.Text))
            {
                hasAnyPingStats = true;

                AddReferenceIpLine(lines, "Secondary Comms Eth IP", IgsdSecondaryCommsEthernetIpTextBox.Text);
                AddReferenceIpLine(lines, "Secondary RTU IP", IgsdSecondaryRtuIpTextBox.Text);
            }

            return hasAnyPingStats
                ? string.Join(Environment.NewLine, lines)
                : string.Empty;
        }

        private static bool TryAddPingWriteUpBlock(List<string> lines, string label, string? ip, string? summary)
        {
            var cleanIp = (ip ?? string.Empty).Trim();
            var cleanSummary = CleanPingSummaryForWriteUp(summary);

            if (string.IsNullOrWhiteSpace(cleanIp) ||
                cleanIp == "—" ||
                string.IsNullOrWhiteSpace(cleanSummary))
            {
                return false;
            }

            if (lines.Count > 1)
                lines.Add(string.Empty);

            lines.Add($"{label} ({cleanIp}) - {cleanSummary}");
            return true;
        }

        private static string CleanPingSummaryForWriteUp(string? summary)
        {
            var value = (summary ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("Ready.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Testing...", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("No IP available.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value.TrimEnd('.');
        }

        private static void AddReferenceIpLine(List<string> lines, string label, string? ip)
        {
            var cleanIp = (ip ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanIp) || cleanIp == "—")
                return;

            lines.Add($"{label} ({cleanIp})");
        }

        public Task RunQuickReachabilityTestForAllAsync()
        {
            if (IsIgsdMode)
            {
                return Task.WhenAll(
                    RunQuickReachabilityTestAsync(PrimaryIpTextBox, PrimarySummaryTextBlock),
                    RunQuickReachabilityTestAsync(SecondaryIpTextBox, SecondarySummaryTextBlock));
            }

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