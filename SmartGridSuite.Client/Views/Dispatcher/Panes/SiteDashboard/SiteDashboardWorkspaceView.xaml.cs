using SmartGridSuite.Contracts.Snmp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView : UserControl
    {
        public event EventHandler<string>? WriteUpTextChanged;
        public event EventHandler<string?>? SelectedWorkspaceTabChanged;

        public event EventHandler? RefreshTicketRequested;
        public event EventHandler? RequestCapitalRequested;

        public event EventHandler? RefreshSnmpRequested;
        public event EventHandler? RunSelectedSnmpRequested;

        public event EventHandler? SetSelectedSnmpRequested;
        public event EventHandler? SnmpTargetChanged;

        private string? _snmpPrimaryIp;
        private string? _snmpLanIp;
        private string? _snmpSecondaryIp;

        public long CurrentTicketId { get; set; }

        private bool _syncingWorkspaceTab;

        public SiteDashboardWorkspaceView()
        {
            InitializeComponent();
            WriteUpTextBox.TextChanged += WriteUpTextBox_TextChanged;
            Reset();
        }

        private static readonly Regex IpRegex =
            new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);

        private string _ticketInfoText = string.Empty;

        public string TicketInfoText
        {
            get => _ticketInfoText;
            set
            {
                _ticketInfoText = value ?? string.Empty;
                ApplyTicketInfo(_ticketInfoText);
            }
        }

        public string TopAccessTitle
        {
            get => TopAccessTitleTextBlock.Text;
            set => TopAccessTitleTextBlock.Text =
                string.IsNullOrWhiteSpace(value)
                    ? "TOP Access"
                    : value.Trim();
        }

        public string TopInfoText
        {
            get => TopInfoTextBox.Text;
            set
            {
                TopInfoTextBox.Text = value ?? string.Empty;
                RefreshTopAccessPanel();
            }
        }

        public string WriteUpText
        {
            get => WriteUpTextBox.Text;
            set => WriteUpTextBox.Text = value ?? string.Empty;
        }

        public string EquipmentText
        {
            get => EquipmentTextBox.Text;
            set
            {
                EquipmentTextBox.Text = value ?? string.Empty;
                RefreshEquipmentCards();
                RefreshOldSerialFromSelection();
            }
        }

        public string SelectedWorkspaceTabKey
        {
            get
            {
                if (WorkspaceTabControl.SelectedItem is TabItem item)
                    return item.Tag?.ToString() ?? "TopWriteUp";

                return "TopWriteUp";
            }
        }

        public void SetSelectedWorkspaceTab(string? tabKey)
        {
            var desired = string.IsNullOrWhiteSpace(tabKey) ? "TopWriteUp" : tabKey;

            _syncingWorkspaceTab = true;

            try
            {
                foreach (var item in WorkspaceTabControl.Items.OfType<TabItem>())
                {
                    if (string.Equals(item.Tag?.ToString(), desired, StringComparison.OrdinalIgnoreCase))
                    {
                        WorkspaceTabControl.SelectedItem = item;
                        return;
                    }
                }

                WorkspaceTabControl.SelectedIndex = 0;
            }
            finally
            {
                _syncingWorkspaceTab = false;
            }
        }

        public void SetHistoryRows(IEnumerable<SiteDashboardHistoryRowViewModel> rows)
        {
            HistoryDataGrid.ItemsSource = rows?.ToList() ?? new List<SiteDashboardHistoryRowViewModel>();
            HistoryDataGrid.SelectedItem = null;
            NarrativeTextBlock.Text = string.Empty;
        }

        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is SiteDashboardHistoryRowViewModel row)
                NarrativeTextBlock.Text = CleanNarrativeText(row.NarrativeText);
            else
                NarrativeTextBlock.Text = string.Empty;
        }

        //Removes \n\n in narrative texts
        private static string CleanNarrativeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            while (normalized.Contains("\n\n\n"))
                normalized = normalized.Replace("\n\n\n", "\n\n");

            return normalized.Trim();
        }

        //SNMP
        public void ResetSnmp()
        {
            _snmpPrimaryIp = null;
            _snmpLanIp = null;
            _snmpSecondaryIp = null;

            SnmpSupportTextBlock.Text = "No site loaded.";
            SnmpProfileTextBlock.Text = "—";
            SnmpFamilyTextBlock.Text = "—";
            SnmpTargetTextBox.Text = string.Empty;
            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueTextBox.IsEnabled = false;
            SetSelectedSnmpButton.IsEnabled = false;
            SnmpSetHintTextBlock.Text = "Select a writable OID to enable setting a value.";
            SnmpOidDataGrid.ItemsSource = null;
            SnmpPreviewTextBox.Text = string.Empty;
            RefreshSnmpButton.IsEnabled = true;
        }

        public void SetSnmpContext(bool supported, string supportMessage, string deviceFamily, string profileName, string? primaryIp, 
            string? lanIp, string? secondaryIp, string? targetIp)
        {
            _snmpPrimaryIp = primaryIp;
            _snmpLanIp = lanIp;
            _snmpSecondaryIp = secondaryIp;

            SnmpSupportTextBlock.Text = string.IsNullOrWhiteSpace(supportMessage) ? "—" : supportMessage;
            SnmpFamilyTextBlock.Text = string.IsNullOrWhiteSpace(deviceFamily) ? "—" : deviceFamily;
            SnmpProfileTextBlock.Text = string.IsNullOrWhiteSpace(profileName) ? "—" : profileName;

            SnmpTargetTextBox.Text = string.IsNullOrWhiteSpace(targetIp)
                ? (primaryIp ?? string.Empty)
                : targetIp;

            RefreshSnmpButton.IsEnabled = true;
        }

        public void SetSnmpOids(IEnumerable<SnmpOidConfigDto> oids)
        {
            SnmpOidDataGrid.ItemsSource = oids?.ToList() ?? new List<SnmpOidConfigDto>();
            SnmpOidDataGrid.SelectedItem = null;
            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueTextBox.IsEnabled = false;
            SetSelectedSnmpButton.IsEnabled = false;
            SnmpSetHintTextBlock.Text = "Select a writable OID to enable setting a value.";
            SnmpPreviewTextBox.Text = string.Empty;
        }

        private void RefreshSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshSnmpRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UseSnmpPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            SnmpTargetTextBox.Text = _snmpPrimaryIp ?? string.Empty;
        }

        private void UseSnmpLanButton_Click(object sender, RoutedEventArgs e)
        {
            SnmpTargetTextBox.Text = _snmpLanIp ?? string.Empty;
        }

        private void UseSnmpSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            SnmpTargetTextBox.Text = _snmpSecondaryIp ?? string.Empty;
        }

        private void SnmpOidDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SnmpOidDataGrid.SelectedItem is not SnmpOidConfigDto oid)
            {
                UpdateSnmpWritableUi(null);
                SnmpPreviewTextBox.Text = string.Empty;
                return;
            }

            UpdateSnmpWritableUi(oid);
            SnmpPreviewTextBox.Text = BuildSnmpPreview(oid);
        }

        private static string BuildSnmpPreview(SnmpOidConfigDto oid)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Category: {oid.Category}");
            sb.AppendLine($"Label: {oid.Label}");
            sb.AppendLine($"OID: {oid.Oid}");
            sb.AppendLine($"Type: {oid.ValueType}");
            sb.AppendLine($"Decode Mode: {oid.DecodeMode}");
            sb.AppendLine($"Writable: {(oid.IsWritable ? "Yes" : "No")}");
            sb.AppendLine($"Show in Workspace: {(oid.ShowInWorkspace ? "Yes" : "No")}");

            if (oid.ShowRawValueAlongsideDecoded)
                sb.AppendLine("Show Raw Alongside Decoded: Yes");

            if (oid.DecodeValues is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("Decoder Values:");

                foreach (var row in oid.DecodeValues.OrderBy(x => x.SortOrder).ThenBy(x => x.RawValue))
                    sb.AppendLine($"  {row.RawValue} = {row.DisplayText}");
            }

            return sb.ToString().TrimEnd();
        }

        private void RunSelectedSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            RunSelectedSnmpRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ShowSnmpPollResult(SnmpRunResultDto? result)
        {
            if (result is null)
            {
                SnmpPreviewTextBox.Text = "No SNMP result.";
                return;
            }

            if (!result.Success)
            {
                SnmpPreviewTextBox.Text =
                    $"SNMP poll failed.{Environment.NewLine}{Environment.NewLine}" +
                    $"Target: {result.TargetIp}{Environment.NewLine}" +
                    $"OID: {result.Oid}{Environment.NewLine}" +
                    $"Error: {result.ErrorMessage}";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Target: {result.TargetIp}");
            sb.AppendLine($"Profile: {result.ProfileName}");
            sb.AppendLine($"Label: {result.Label}");
            sb.AppendLine($"OID: {result.Oid}");
            sb.AppendLine($"Decode Mode: {result.DecodeMode}");
            sb.AppendLine();
            sb.AppendLine($"Raw Value: {result.RawValue}");
            sb.AppendLine($"Display Value: {result.DisplayValue}");

            SnmpPreviewTextBox.Text = sb.ToString().TrimEnd();
        }

        public void ShowSnmpSetResult(SnmpSetResultDto? result)
        {
            if (result is null)
            {
                SnmpPreviewTextBox.Text = "No SNMP set result.";
                return;
            }

            if (!result.Success)
            {
                SnmpPreviewTextBox.Text =
                    $"SNMP set failed.{Environment.NewLine}{Environment.NewLine}" +
                    $"Target: {result.TargetIp}{Environment.NewLine}" +
                    $"OID: {result.Oid}{Environment.NewLine}" +
                    $"Requested Value: {result.RequestedValue}{Environment.NewLine}" +
                    $"Error: {result.ErrorMessage}";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Target: {result.TargetIp}");
            sb.AppendLine($"Profile: {result.ProfileName}");
            sb.AppendLine($"Label: {result.Label}");
            sb.AppendLine($"OID: {result.Oid}");
            sb.AppendLine($"Decode Mode: {result.DecodeMode}");
            sb.AppendLine();
            sb.AppendLine($"Requested Value: {result.RequestedValue}");
            sb.AppendLine($"Raw Value: {result.RawValue}");
            sb.AppendLine($"Display Value: {result.DisplayValue}");

            SnmpPreviewTextBox.Text = sb.ToString().TrimEnd();
        }

        public string GetSnmpTargetIp()
        {
            return (SnmpTargetTextBox.Text ?? string.Empty).Trim();
        }

        public string GetSnmpSetValue()
        {
            return (SnmpSetValueTextBox.Text ?? string.Empty).Trim();
        }

        public SnmpOidConfigDto? GetSelectedSnmpOid()
        {
            return SnmpOidDataGrid.SelectedItem as SnmpOidConfigDto;
        }

        private void SnmpTargetTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SnmpTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetSelectedSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedSnmpRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateSnmpWritableUi(SnmpOidConfigDto? oid)
        {
            var writable = oid?.IsWritable == true;

            SnmpSetValueTextBox.IsEnabled = writable;
            SetSelectedSnmpButton.IsEnabled = writable;

            if (oid is null)
            {
                SnmpSetHintTextBlock.Text = "Select a writable OID to enable setting a value.";
                return;
            }

            if (!writable)
            {
                SnmpSetHintTextBlock.Text = "Selected OID is read-only.";
                return;
            }

            SnmpSetHintTextBlock.Text = $"Writable OID. Value type: {oid.ValueType}. Enter the raw value to set.";
        }

        //End of SNMP

        public void Reset()
        {
            TopInfoText = string.Empty;
            TopAccessTitle = "TOP Access";
            WriteUpText = string.Empty;
            EquipmentText = string.Empty;
            SetHistoryRows(Array.Empty<SiteDashboardHistoryRowViewModel>());
            SetSelectedWorkspaceTab("TopWriteUp");

            TicketInfoText = string.Empty;

            ResetSnmp();
        }

        private void WorkspaceTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingWorkspaceTab)
                return;

            if (WorkspaceTabControl.SelectedItem is not TabItem tab)
                return;

            var key = tab.Tag as string;

            TopWriteUpPanel.Visibility = key == "TopWriteUp" ? Visibility.Visible : Visibility.Collapsed;
            SiteHistoryPanel.Visibility = key == "SiteHistory" ? Visibility.Visible : Visibility.Collapsed;
            EquipmentPanel.Visibility = key == "Equipment" ? Visibility.Visible : Visibility.Collapsed;
            SnmpPanel.Visibility = key == "SNMPTool" ? Visibility.Visible : Visibility.Collapsed;

            SelectedWorkspaceTabChanged?.Invoke(this, key);
        }

        private void WriteUpTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            WriteUpTextChanged?.Invoke(this, WriteUpTextBox.Text);
        }

        private void ReplacementItemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshOldSerialFromSelection();
        }

        private void RefreshOldSerialFromSelection()
        {
            var label = (ReplacementItemComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            OldSerialTextBox.Text = FindEquipmentValueByLabel(label) ?? string.Empty;
        }

        private string? FindEquipmentValueByLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;

            var lines = (EquipmentTextBox.Text ?? string.Empty)
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

            return null;
        }

        private void AddReplacementToWriteUpButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (ReplacementItemComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var oldSerial = OldSerialTextBox.Text?.Trim();
            var newSerial = NewSerialTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(item))
            {
                MessageBox.Show("Choose the serialized item that was replaced.",
                    "Replacement Entry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(newSerial))
            {
                MessageBox.Show("Enter the new serial number.",
                    "Replacement Entry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var line = string.IsNullOrWhiteSpace(oldSerial)
                ? $"Equipment replaced: {item} | New SN: {newSerial}"
                : $"Equipment replaced: {item} | Old SN: {oldSerial} | New SN: {newSerial}";

            if (!string.IsNullOrWhiteSpace(WriteUpTextBox.Text))
                WriteUpTextBox.AppendText(Environment.NewLine + line);
            else
                WriteUpTextBox.Text = line;

            WriteUpTextBox.ScrollToEnd();
            WriteUpTextChanged?.Invoke(this, WriteUpTextBox.Text);

            NewSerialTextBox.Clear();
        }

        private void RefreshEquipmentCards()
        {
            var lines = (EquipmentTextBox.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            EnclosureCardTextBox.Text = BuildEquipmentCardText(lines, "Enclosure ");
            PrimaryCardTextBox.Text = BuildEquipmentCardText(lines, "Primary ");
            SecondaryCardTextBox.Text = BuildEquipmentCardText(lines, "Secondary ");
            AntennaCardTextBox.Text = BuildEquipmentCardText(lines, "Antenna ");
        }

        private static string BuildEquipmentCardText(List<string> lines, string prefix)
        {
            var matched = lines
                .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(x => x[prefix.Length..].Trim())
                .ToList();

            return matched.Count == 0
                ? "—"
                : string.Join(Environment.NewLine, matched);
        }

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

        private void OpenTopIpAButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopIpInBrowser(TopIpATextBox.Text);
        }

        private void OpenTopIpBButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopIpInBrowser(TopIpBTextBox.Text);
        }

        private static void OpenTopIpInBrowser(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            try
            {
                Process.Start(new ProcessStartInfo($"https://{ip.Trim()}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open https://{ip}.{Environment.NewLine}{ex.Message}",
                    "Open TOP Web GUI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
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

        //Tickets
        private void ApplyTicketInfo(string rawText)
        {
            TicketNotificationNameTextBlock.Text = GetTicketFieldValue(rawText, "Notification Name");
            TicketNotificationNumberTextBlock.Text = GetTicketFieldValue(rawText, "Notification #");
            TicketProblemIssueTextBlock.Text = GetTicketFieldValue(rawText, "Problem/Issue");
            TicketWorkOrderTextBlock.Text = GetTicketFieldValue(rawText, "Work Order");
            TicketWorkOrderTypeTextBlock.Text = GetTicketFieldValue(rawText, "Work Order Type");
            TicketAssignedToTextBlock.Text = GetTicketFieldValue(rawText, "Assigned To");
            TicketDateCreatedTextBlock.Text = GetTicketFieldValue(rawText, "Date Created");
            TicketStatusTextBlock.Text = GetTicketFieldValue(rawText, "Current Status");

            ApplyWorkOrderTypeBadgeAndButton(TicketWorkOrderTypeTextBlock.Text);
            ApplyTicketStatusBadge(TicketStatusTextBlock.Text);

            if (string.IsNullOrWhiteSpace(TicketNotificationNameTextBlock.Text))
                TicketNotificationNameTextBlock.Text = "No ticket data returned yet.";

            if (string.IsNullOrWhiteSpace(TicketNotificationNumberTextBlock.Text))
                TicketNotificationNumberTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketProblemIssueTextBlock.Text))
                TicketProblemIssueTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketWorkOrderTextBlock.Text))
                TicketWorkOrderTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketWorkOrderTypeTextBlock.Text))
                TicketWorkOrderTypeTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketAssignedToTextBlock.Text))
                TicketAssignedToTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketDateCreatedTextBlock.Text))
                TicketDateCreatedTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketStatusTextBlock.Text))
                TicketStatusTextBlock.Text = "—";
        }

        private static string GetTicketFieldValue(string rawText, string label)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            var lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

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

        private void RefreshTicketButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshTicketRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RequestCapitalButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentTicketId <= 0)
            {
                MessageBox.Show(
                    "No ticket is currently selected for this site.",
                    "Request Capital",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            RequestCapitalRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CopyTicketNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            CopyTicketValue(TicketNotificationNumberTextBlock.Text);
        }

        private void CopyTicketWorkOrderButton_Click(object sender, RoutedEventArgs e)
        {
            CopyTicketValue(TicketWorkOrderTextBlock.Text);
        }

        private static void CopyTicketValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return;

            Clipboard.SetText(value);
        }

        private void ApplyWorkOrderTypeBadgeAndButton(string workOrderType)
        {
            var value = (workOrderType ?? string.Empty).Trim();

            TicketWorkOrderTypeBadge.ClearValue(Border.BackgroundProperty);
            TicketWorkOrderTypeBadge.ClearValue(Border.BorderBrushProperty);
            RequestCapitalButton.Visibility = Visibility.Collapsed;

            if (value.Equals("Capital", StringComparison.OrdinalIgnoreCase))
            {
                TicketWorkOrderTypeBadge.Background = new SolidColorBrush(Color.FromRgb(253, 236, 234));
                TicketWorkOrderTypeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));
                return;
            }

            if (value.Equals("Maintenance", StringComparison.OrdinalIgnoreCase))
            {
                TicketWorkOrderTypeBadge.Background = new SolidColorBrush(Color.FromRgb(236, 239, 241));
                TicketWorkOrderTypeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(144, 164, 174));
                RequestCapitalButton.Visibility = Visibility.Visible;
            }
        }

        private void ApplyTicketStatusBadge(string status)
        {
            var value = (status ?? string.Empty).Trim();

            TicketStatusBadge.ClearValue(Border.BackgroundProperty);
            TicketStatusBadge.ClearValue(Border.BorderBrushProperty);

            if (value.Equals("Awaiting Capital", StringComparison.OrdinalIgnoreCase))
            {
                TicketStatusBadge.Background = new SolidColorBrush(Color.FromRgb(255, 248, 225));
                TicketStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 180, 0));
                return;
            }

            if (value.Equals("Assigned", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("In Progress", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Open", StringComparison.OrdinalIgnoreCase))
            {
                TicketStatusBadge.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
                TicketStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                return;
            }

            if (value.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                TicketStatusBadge.Background = new SolidColorBrush(Color.FromRgb(236, 239, 241));
                TicketStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(144, 164, 174));
            }
        }
    }
}