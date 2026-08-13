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
        private NetworkPingSessionState _pingState = new();

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
            StopPingSession(_pingState);

            _pingState =
                new NetworkPingSessionState();

            ResetDisplay();
        }

        public void ResetDisplay()
        {
            IsIgsdMode = false;

            PrimaryPingLabel =
                "Primary";

            LanPingLabel =
                "LAN";

            SecondaryPingLabel =
                "Secondary";

            PrimaryIp =
                string.Empty;

            LanIp =
                string.Empty;

            SecondaryIp =
                string.Empty;

            IgsdPrimaryRtuIp =
                string.Empty;

            IgsdPrimaryCommsEthernetIp =
                string.Empty;

            IgsdSecondaryCommsEthernetIp =
                string.Empty;

            IgsdSecondaryRtuIp =
                string.Empty;

            PingCountTextBox.Text =
                string.Empty;

            PrimarySummaryTextBlock.Text =
                "Ready.";

            LanSummaryTextBlock.Text =
                "Ready.";

            SecondarySummaryTextBlock.Text =
                "Ready.";

            PrimaryResultsTextBox.Text =
                string.Empty;

            LanResultsTextBox.Text =
                string.Empty;

            SecondaryResultsTextBox.Text =
                string.Empty;

            SiteHeader =
                string.Empty;

            ClearIpTestState(
                PrimaryIpTextBox);

            ClearIpTestState(
                LanIpTextBox);

            ClearIpTestState(
                SecondaryIpTextBox);

            _primaryTestSuccessful =
                null;

            _lanTestSuccessful =
                null;

            _secondaryTestSuccessful =
                null;

            ApplyLayoutMode();

            RefreshPingButtonStates();
        }

        public NetworkPingSessionState GetPingSessionState()
        {
            _pingState.PingCount =
                PingCountTextBox.Text ??
                string.Empty;

            CapturePingTargetState(
                _pingState.Primary,
                PrimaryIpTextBox,
                PrimaryResultsTextBox,
                PrimarySummaryTextBlock,
                _primaryTestSuccessful);

            CapturePingTargetState(
                _pingState.Lan,
                LanIpTextBox,
                LanResultsTextBox,
                LanSummaryTextBlock,
                _lanTestSuccessful);

            CapturePingTargetState(
                _pingState.Secondary,
                SecondaryIpTextBox,
                SecondaryResultsTextBox,
                SecondarySummaryTextBlock,
                _secondaryTestSuccessful);

            _pingState.IgsdPrimaryRtuIp =
                IgsdPrimaryRtuIpTextBox.Text ??
                string.Empty;

            _pingState.IgsdPrimaryCommsEthernetIp =
                IgsdPrimaryCommsEthernetIpTextBox.Text ??
                string.Empty;

            _pingState.IgsdSecondaryCommsEthernetIp =
                IgsdSecondaryCommsEthernetIpTextBox.Text ??
                string.Empty;

            _pingState.IgsdSecondaryRtuIp =
                IgsdSecondaryRtuIpTextBox.Text ??
                string.Empty;

            return _pingState;
        }

        public bool HasIpAddressChanges(
            string? originalPrimaryIp,
            string? originalLanIp,
            string? originalSecondaryIp,
            string? originalIgsdPrimaryRtuIp,
            string? originalIgsdPrimaryCommsEthernetIp,
            string? originalIgsdSecondaryCommsEthernetIp,
            string? originalIgsdSecondaryRtuIp)
        {
            return GetIpAddressChangeWriteUpLines(
                    originalPrimaryIp,
                    originalLanIp,
                    originalSecondaryIp,
                    originalIgsdPrimaryRtuIp,
                    originalIgsdPrimaryCommsEthernetIp,
                    originalIgsdSecondaryCommsEthernetIp,
                    originalIgsdSecondaryRtuIp)
                .Count > 0;
        }

        public IReadOnlyList<string> GetIpAddressChangeWriteUpLines(
            string? originalPrimaryIp,
            string? originalLanIp,
            string? originalSecondaryIp,
            string? originalIgsdPrimaryRtuIp,
            string? originalIgsdPrimaryCommsEthernetIp,
            string? originalIgsdSecondaryCommsEthernetIp,
            string? originalIgsdSecondaryRtuIp)
        {
            var lines =
                new List<string>();

            AddIpChangeWriteUpLine(
                lines,
                PrimaryPingLabel,
                PrimaryIpTextBox.Text,
                originalPrimaryIp);

            AddIpChangeWriteUpLine(
                lines,
                LanPingLabel,
                LanIpTextBox.Text,
                originalLanIp);

            AddIpChangeWriteUpLine(
                lines,
                SecondaryPingLabel,
                SecondaryIpTextBox.Text,
                originalSecondaryIp);

            AddIpChangeWriteUpLine(
                lines,
                "Primary RTU",
                IgsdPrimaryRtuIpTextBox.Text,
                originalIgsdPrimaryRtuIp);

            AddIpChangeWriteUpLine(
                lines,
                "Primary Comms Ethernet",
                IgsdPrimaryCommsEthernetIpTextBox.Text,
                originalIgsdPrimaryCommsEthernetIp);

            AddIpChangeWriteUpLine(
                lines,
                "Secondary Comms Ethernet",
                IgsdSecondaryCommsEthernetIpTextBox.Text,
                originalIgsdSecondaryCommsEthernetIp);

            AddIpChangeWriteUpLine(
                lines,
                "Secondary RTU",
                IgsdSecondaryRtuIpTextBox.Text,
                originalIgsdSecondaryRtuIp);

            return lines;
        }

        private static void AddIpChangeWriteUpLine(
            List<string> lines,
            string label,
            string? currentValue,
            string? originalValue)
        {
            var current =
                SnapshotIp(currentValue);

            var original =
                SnapshotIp(originalValue);

            if (string.Equals(
                    current,
                    original,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cleanLabel =
                string.IsNullOrWhiteSpace(label)
                    ? "Network"
                    : label.Trim();

            var displayValue =
                string.IsNullOrWhiteSpace(current)
                    ? "(cleared)"
                    : current;

            lines.Add(
                $"New {cleanLabel} IP: {displayValue}");
        }

        private static bool IpValuesMatch(
            string? currentValue,
            string? originalValue)
        {
            return string.Equals(
                SnapshotIp(currentValue),
                SnapshotIp(originalValue),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void CapturePingTargetState(
            NetworkPingTargetState state,
            TextBox ipTextBox,
            TextBox resultsTextBox,
            TextBlock summaryTextBlock,
            bool? testSuccessful)
        {
            state.Ip =
                SnapshotIp(
                    ipTextBox.Text);

            state.Results =
                resultsTextBox.Text ??
                string.Empty;

            state.Summary =
                summaryTextBlock.Text ??
                "Ready.";

            state.TestSuccessful =
                testSuccessful;
        }

        public void RestorePingSessionState(NetworkPingSessionState? state)
        {
            _pingState =
                state ??
                new NetworkPingSessionState();

            if (state is null)
            {
                RefreshPingButtonStates();
                return;
            }

            PingCountTextBox.Text =
                state.PingCount ??
                string.Empty;

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

            if (!string.IsNullOrWhiteSpace(
                    state.IgsdPrimaryRtuIp))
            {
                IgsdPrimaryRtuIpTextBox.Text =
                    state.IgsdPrimaryRtuIp;
            }

            if (!string.IsNullOrWhiteSpace(
                    state.IgsdPrimaryCommsEthernetIp))
            {
                IgsdPrimaryCommsEthernetIpTextBox.Text =
                    state.IgsdPrimaryCommsEthernetIp;
            }

            if (!string.IsNullOrWhiteSpace(
                    state.IgsdSecondaryCommsEthernetIp))
            {
                IgsdSecondaryCommsEthernetIpTextBox.Text =
                    state.IgsdSecondaryCommsEthernetIp;
            }

            if (!string.IsNullOrWhiteSpace(
                    state.IgsdSecondaryRtuIp))
            {
                IgsdSecondaryRtuIpTextBox.Text =
                    state.IgsdSecondaryRtuIp;
            }

            RefreshPingButtonStates();
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

        private async void PingPrimaryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ToggleSinglePingAsync(
                _pingState.Primary,
                PrimaryIp);
        }

        private async void PingLanButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ToggleSinglePingAsync(
                _pingState.Lan,
                LanIp);
        }

        private async void PingSecondaryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await ToggleSinglePingAsync(
                _pingState.Secondary,
                SecondaryIp);
        }

        private async Task ToggleSinglePingAsync(
            NetworkPingTargetState target, string ipAddress)
        {
            var ownerState =
                _pingState;

            if (target.PingCts is not null)
            {
                CancelAndDispose(
                    target);

                RefreshActivePingUi(
                    ownerState);

                return;
            }

            GetPingSessionState();

            target.Ip =
                SnapshotIp(
                    ipAddress);

            await RunSinglePingAsync(
                ownerState,
                target);
        }

        private async Task RunSinglePingAsync(
            NetworkPingSessionState ownerState, NetworkPingTargetState target)
        {
            if (target.PingCts is not null)
                return;

            var cts =
                new CancellationTokenSource();

            target.PingCts =
                cts;

            RefreshActivePingUi(
                ownerState);

            try
            {
                await PingTargetForSessionAsync(
                    ownerState,
                    target,
                    cts.Token);
            }
            finally
            {
                if (ReferenceEquals(
                        target.PingCts,
                        cts))
                {
                    target.PingCts =
                        null;

                    cts.Dispose();
                }

                RefreshActivePingUi(
                    ownerState);
            }
        }

        private async void PingAllButton_Click(object sender, RoutedEventArgs e)
        {
            var ownerState =
                _pingState;

            if (IsAnyPingRunning(
                    ownerState))
            {
                StopPingSession(
                    ownerState);

                return;
            }

            GetPingSessionState();

            ownerState.Primary.Ip =
                SnapshotIp(
                    PrimaryIp);

            ownerState.Lan.Ip =
                SnapshotIp(
                    LanIp);

            ownerState.Secondary.Ip =
                SnapshotIp(
                    SecondaryIp);

            var includeLan =
                !IsIgsdMode;

            var tasks =
                new List<Task>
                {
            RunSinglePingAsync(
                ownerState,
                ownerState.Primary),

            RunSinglePingAsync(
                ownerState,
                ownerState.Secondary)
                };

            if (includeLan)
            {
                tasks.Insert(
                    1,
                    RunSinglePingAsync(
                        ownerState,
                        ownerState.Lan));
            }

            await Task.WhenAll(
                tasks);
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
            ipTextBox.ClearValue(
                Control.BackgroundProperty);

            ipTextBox.ClearValue(
                Control.BorderBrushProperty);

            ipTextBox.ClearValue(
                Control.ForegroundProperty);

            ipTextBox.ClearValue(
                TextBox.CaretBrushProperty);

            ipTextBox.ClearValue(
                Control.BorderThicknessProperty);
        }

        /*
         * Compatibility wrapper for existing quick-test callers.
         */
        private async Task PingTargetAsync(
            string ipText,
            TextBlock summaryTextBlock,
            TextBox resultsTextBox,
            CancellationToken cancellationToken)
        {
            var ownerState =
                GetPingSessionState();

            var target =
                ResolvePingTargetState(
                    resultsTextBox);

            target.Ip =
                SnapshotIp(
                    ipText);

            await PingTargetForSessionAsync(
                ownerState,
                target,
                cancellationToken);
        }

        private async Task PingTargetForSessionAsync(
            NetworkPingSessionState ownerState,
            NetworkPingTargetState target,
            CancellationToken cancellationToken)
        {
            target.Results =
                string.Empty;

            var ip =
                (target.Ip ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip) ||
                ip == "—")
            {
                target.Summary =
                    "No IP available.";

                RenderPingTargetIfActive(
                    ownerState,
                    target);

                return;
            }

            var requestedCount =
                ParsePingCount(
                    ownerState.PingCount);

            var sent =
                0;

            var lost =
                0;

            using var ping =
                new Ping();

            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       (requestedCount is null ||
                        sent < requestedCount.Value))
                {
                    string line;

                    try
                    {
                        var reply =
                            await ping.SendPingAsync(
                                ip,
                                1500);

                        sent++;

                        if (reply.Status ==
                            IPStatus.Success)
                        {
                            line =
                                $"{DateTime.Now:HH:mm:ss} {ip}: Time={reply.RoundtripTime} ms";
                        }
                        else
                        {
                            lost++;

                            line =
                                $"{DateTime.Now:HH:mm:ss} {ip}: {reply.Status}";
                        }
                    }
                    catch (Exception ex)
                    {
                        sent++;
                        lost++;

                        line =
                            $"{DateTime.Now:HH:mm:ss} {ip}: {ex.Message}";
                    }

                    target.Results =
                        string.IsNullOrWhiteSpace(
                            target.Results)
                            ? line
                            : target.Results +
                              Environment.NewLine +
                              line;

                    var lossPercent =
                        sent == 0
                            ? 0
                            : (int)Math.Round(
                                (double)lost * 100 /
                                sent);

                    target.Summary =
                        $"Sent = {sent}, Lost = {lost} ({lossPercent}% loss).";

                    RenderPingTargetIfActive(
                        ownerState,
                        target);

                    if (requestedCount is null)
                    {
                        await Task.Delay(
                            1000,
                            cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (sent == 0)
                {
                    target.Summary =
                        "Stopped.";

                    RenderPingTargetIfActive(
                        ownerState,
                        target);
                }
            }
        }

        private NetworkPingTargetState ResolvePingTargetState(
            TextBox resultsTextBox)
        {
            if (ReferenceEquals(
                    resultsTextBox,
                    PrimaryResultsTextBox))
            {
                return _pingState.Primary;
            }

            if (ReferenceEquals(
                    resultsTextBox,
                    LanResultsTextBox))
            {
                return _pingState.Lan;
            }

            return _pingState.Secondary;
        }

        private void RenderPingTargetIfActive(
            NetworkPingSessionState ownerState,
            NetworkPingTargetState target)
        {
            /*
             * The ping continues updating its original tab state while that tab
             * is hidden. Only update visible controls when that same state is
             * currently selected.
             */
            if (!ReferenceEquals(
                    _pingState,
                    ownerState))
            {
                return;
            }

            TextBox resultsTextBox;
            TextBlock summaryTextBlock;

            if (ReferenceEquals(
                    ownerState.Primary,
                    target))
            {
                resultsTextBox =
                    PrimaryResultsTextBox;

                summaryTextBlock =
                    PrimarySummaryTextBlock;
            }
            else if (ReferenceEquals(
                         ownerState.Lan,
                         target))
            {
                resultsTextBox =
                    LanResultsTextBox;

                summaryTextBlock =
                    LanSummaryTextBlock;
            }
            else
            {
                resultsTextBox =
                    SecondaryResultsTextBox;

                summaryTextBlock =
                    SecondarySummaryTextBlock;
            }

            resultsTextBox.Text =
                target.Results ??
                string.Empty;

            summaryTextBlock.Text =
                target.Summary ??
                "Ready.";

            resultsTextBox.ScrollToEnd();
        }

        private void RefreshActivePingUi(
            NetworkPingSessionState ownerState)
        {
            if (!ReferenceEquals(
                    _pingState,
                    ownerState))
            {
                return;
            }

            RenderPingTargetIfActive(
                ownerState,
                ownerState.Primary);

            RenderPingTargetIfActive(
                ownerState,
                ownerState.Lan);

            RenderPingTargetIfActive(
                ownerState,
                ownerState.Secondary);

            RefreshPingButtonStates();
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
            return ParsePingCount(
                PingCountTextBox.Text);
        }

        private static int? ParsePingCount(
            string? rawValue)
        {
            var raw =
                rawValue?.Trim();

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (!int.TryParse(
                    raw,
                    out var count))
            {
                return 3;
            }

            if (count <= 0 ||
                count >= 999999)
            {
                return 3;
            }

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
            StopPingSession(
                _pingState);
        }

        public void StopPingSession(
            NetworkPingSessionState? state)
        {
            if (state is null)
                return;

            CancelAndDispose(
                state.Primary);

            CancelAndDispose(
                state.Lan);

            CancelAndDispose(
                state.Secondary);

            if (ReferenceEquals(
                    _pingState,
                    state))
            {
                RefreshPingButtonStates();
            }
        }

        private static void CancelAndDispose(
            NetworkPingTargetState target)
        {
            var cts =
                target.PingCts;

            target.PingCts =
                null;

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
        }

        private bool IsAnyPingRunning()
        {
            return IsAnyPingRunning(
                _pingState);
        }

        private static bool IsAnyPingRunning(
            NetworkPingSessionState state)
        {
            return state.Primary.PingCts is not null ||
                   state.Lan.PingCts is not null ||
                   state.Secondary.PingCts is not null;
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

        private void RefreshPingButtonStates()
        {
            SetPingButtonState(
                PrimaryPingButton,
                _pingState.Primary.PingCts is not null,
                normalText: "Ping",
                normalStyleKey: "NetworkMiniButtonStyle");

            SetPingButtonState(
                LanPingButton,
                _pingState.Lan.PingCts is not null,
                normalText: "Ping",
                normalStyleKey: "NetworkMiniButtonStyle");

            SetPingButtonState(
                SecondaryPingButton,
                _pingState.Secondary.PingCts is not null,
                normalText: "Ping",
                normalStyleKey: "NetworkMiniButtonStyle");

            SetPingButtonState(
                PingAllButton,
                IsAnyPingRunning(),
                normalText: "Ping All",
                normalStyleKey: "NetworkPrimaryMiniButtonStyle");
        }

        private void SetPingButtonState(Button button, bool isRunning, string normalText, string normalStyleKey)
        {
            button.Content = isRunning ? "Stop" : normalText;

            button.Style = isRunning
                ? (Style)FindResource("NetworkDangerMiniButtonStyle")
                : (Style)FindResource(normalStyleKey);
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
            ClearIpTestState(ipTextBox);

            if (isSuccessful is null)
                return;

            var resourcePrefix =
                isSuccessful.Value
                    ? "NetworkPingSuccess"
                    : "NetworkPingFailure";

            ipTextBox.SetResourceReference(
                Control.BackgroundProperty,
                $"{resourcePrefix}Bg");

            ipTextBox.SetResourceReference(
                Control.BorderBrushProperty,
                $"{resourcePrefix}Border");

            ipTextBox.SetResourceReference(
                Control.ForegroundProperty,
                $"{resourcePrefix}Text");

            ipTextBox.SetResourceReference(
                TextBox.CaretBrushProperty,
                $"{resourcePrefix}Text");

            ipTextBox.BorderThickness =
                new Thickness(1.5);
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

            if (string.IsNullOrWhiteSpace(cleanIp) || cleanIp == "—")
                return false;

            var cleanSummary = CleanPingSummaryForWriteUp(summary);

            if (string.IsNullOrWhiteSpace(cleanSummary))
                lines.Add($"{label} ({cleanIp})");
            else
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