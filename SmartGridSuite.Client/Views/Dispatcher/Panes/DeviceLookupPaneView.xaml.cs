using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Dispatcher;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class DeviceLookupPaneView : UserControl
    {
        private readonly ApiClient _api =
            ClientAppSettings.CreateApiClient();

        private CancellationTokenSource? _searchCts;

        public DeviceLookupPaneView()
        {
            InitializeComponent();
        }

        private void DeviceLookupPaneView_Unloaded(object sender, RoutedEventArgs e)
        {
            _searchCts?.Cancel();
        }

        private async void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await SearchAsync();
        }

        private async void SearchTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await SearchAsync();
        }

        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is DeviceLookupHistoryDto row)
            {
                HistoryNarrativeTextBox.Text = CleanNarrativeText(row.Narrative);
                HistorySourceTextBlock.Text = string.IsNullOrWhiteSpace(row.SourceType)
                    ? string.Empty
                    : row.SourceType;
            }
            else
            {
                HistoryNarrativeTextBox.Text = string.Empty;
                HistorySourceTextBlock.Text = string.Empty;
            }
        }

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

        private static IReadOnlyList<DeviceLookupRecordDto> BuildDisplayParentRecords(
            IEnumerable<DeviceLookupRecordDto> records)
        {
            return records
                .Select(BuildDisplayParentRecord)
                .Where(x => x.Fields.Count > 0)
                .ToList();
        }

        private static DeviceLookupRecordDto BuildDisplayParentRecord(DeviceLookupRecordDto source)
        {
            var specs = GetDisplayFieldSpecs(source.RecordType);
            if (specs.Length == 0)
            {
                return new DeviceLookupRecordDto
                {
                    Source = source.Source,
                    RecordType = source.RecordType,
                    SiteId = source.SiteId,
                    MatchField = source.MatchField,
                    Fields = source.Fields
                };
            }

            var fields = new List<DeviceLookupFieldDto>();

            foreach (var spec in specs)
            {
                var value = FindFieldValue(source.Fields, spec.SourceNames);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                fields.Add(new DeviceLookupFieldDto
                {
                    Label = spec.DisplayLabel,
                    Value = value
                });
            }

            return new DeviceLookupRecordDto
            {
                Source = source.Source,
                RecordType = source.RecordType,
                SiteId = source.SiteId,
                MatchField = GetDisplayMatchField(source.MatchField, specs),
                Fields = fields
            };
        }

        private static string GetDisplayMatchField(
            string matchField,
            IEnumerable<DisplayFieldSpec> specs)
        {
            foreach (var spec in specs)
            {
                if (spec.SourceNames.Any(x =>
                    string.Equals(x, matchField, StringComparison.OrdinalIgnoreCase)))
                {
                    return spec.DisplayLabel;
                }
            }

            return string.Equals(matchField, "Related", StringComparison.OrdinalIgnoreCase)
                ? "Related"
                : matchField;
        }

        private static string FindFieldValue(
            IEnumerable<DeviceLookupFieldDto> fields,
            params string[] names)
        {
            foreach (var name in names)
            {
                var field = fields.FirstOrDefault(x =>
                    string.Equals(x.Label, name, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(field?.Value))
                    return field.Value.Trim();
            }

            return string.Empty;
        }

        private static DisplayFieldSpec[] GetDisplayFieldSpecs(string recordType)
        {
            if (recordType.Equals("IGSD", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Number", "SiteId", "SiteID"),
                    new("Primary Comms", "PriComm"),
                    new("Primary Comms Model", "PriDigiModl"),
                    new("Secondary Comms", "SecComm"),
                    new("Secondary Comms Model", "SecDigiModl"),
                    new("Primary Comms SN", "RadioSN"),
                    new("Cyberlock SN", "Cyberlock"),
                    new("Site Type", "Config"),
                    new("Primary IP", "RadioIP", "Radio IP"),
                    new("Primary RTU IP", "PriProtLanRtu"),
                    new("IPSEC Modem IP", "PriWanOut"),
                    new("Secondary IP", "SecDigiWanOut"),
                    new("Secondary Eth IP", "SecProtLanDigi"),
                    new("Secondary RTU IP", "SecProtLanRtu")
                ];
            }

            if (recordType.Equals("AMS / MR", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Number", "SiteId", "SiteID"),
                    new("Secondary Equip", "CommEquip"),
                    new("Secondary SN", "iTron_CR_Num"),
                    new("Primary SN", "RadioSN"),
                    new("Primary IP", "RadioIP"),
                    new("LAN IP", "EthernetIP"),
                    new("Primary Type", "RadioFreq")
                ];
            }

            if (recordType.Equals("LTE", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Number", "SiteId", "SiteID"),
                    new("SN", "SN"),
                    new("Model", "Model"),
                    new("Comm Type", "CommType"),
                    new("Carrier", "Carrier1"),
                    new("SIM 1", "SIM1"),
                    new("IP", "IP1")
                ];
            }

            if (recordType.Equals("Radio700 / DACS", StringComparison.OrdinalIgnoreCase))
                return [new("Site Number", "SiteId", "SiteID")];

            if (recordType.Equals("PMR", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Name", "SiteId", "SiteID", "CurrentSiteId"),
                    new("SN", "SN"),
                    new("Username", "UserName"),
                    new("Wifi SSID", "wifiSSID"),
                    new("Password", "CAMPassword", "CAM Password"),
                    new("RFLAN MAC", "RFLANMAC", "RFLAN_MAC", "RFLAN MAC"),
                    new("SIM 1", "ATTSlot1"),
                    new("IMEI", "IMEI"),
                    new("Associated Radio SN", "AssociatedAmsRadioSN"),
                    new("Associated Radio IP", "AssociatedAmsRadioIP"),
                    new("Associated LAN IP", "AssociatedAmsEthernetIP")
                ];
            }

            if (recordType.Equals("Antenna", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Number", "SiteId", "SiteID"),
                    new("SN", "SN")
                ];
            }

            if (recordType.Equals("Enclosure", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Number", "SiteId", "SiteID"),
                    new("SN", "SN"),
                    new("Model", "Model")
                ];
            }

            if (recordType.Equals("Site", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new("Site Number", "Site"),
                    new("Status", "Status"),
                    new("Type", "Type"),
                    new("Primary Comm Type", "Primary Comm Type"),
                    new("Secondary Comm Type", "Secondary Comm Type"),
                    new("Config Description", "Config Description")
                ];
            }

            return [];
        }

        private sealed class DisplayFieldSpec
        {
            public DisplayFieldSpec(string displayLabel, params string[] sourceNames)
            {
                DisplayLabel = displayLabel;
                SourceNames = sourceNames;
            }

            public string DisplayLabel { get; }

            public string[] SourceNames { get; }
        }

        private async Task SearchAsync()
        {
            if (_searchCts != null)
                return;

            var query =
                (SearchTextBox.Text ?? string.Empty)
                    .Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                StatusTextBlock.Text =
                    "Select a search type and enter its identifier.";

                return;
            }

            if (!Enum.TryParse<DeviceLookupSearchType>(
                    (SearchTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var searchType))
                return;

            ParentRecordsItemsControl.ItemsSource = null;
            RelatedSitesItemsControl.ItemsSource = null;
            TicketsDataGrid.ItemsSource = null;
            HistoryDataGrid.ItemsSource = null;
            HistoryDataGrid.SelectedItem = null;
            HistoryNarrativeTextBox.Text = string.Empty;
            HistorySourceTextBlock.Text = string.Empty;

            _searchCts =
                new CancellationTokenSource();

            var ct =
                _searchCts.Token;

            SearchTypeComboBox.IsEnabled = false;
            SearchButton.IsEnabled = false;
            SearchTextBox.IsEnabled = false;

            StatusTextBlock.Text =
                $"Searching for {query}... This may take up to a minute.";

            try
            {
                var result =
                    await _api.GetDeviceLookupAsync(query, searchType, ct);

                if (ct.IsCancellationRequested)
                    return;

                result ??=
                    new DeviceLookupResponseDto
                    {
                        Query = query
                    };

                var displayParentRecords =
                    BuildDisplayParentRecords(result.ParentRecords);

                ParentRecordsItemsControl.ItemsSource =
                    displayParentRecords;

                RelatedSitesItemsControl.ItemsSource =
                    result.RelatedSiteIds;

                TicketsDataGrid.ItemsSource =
                    result.Tickets;

                HistoryDataGrid.ItemsSource =
                    result.SiteHistory;

                var total =
                    displayParentRecords.Count +
                    result.Tickets.Count +
                    result.SiteHistory.Count;

                var summary =
                    total == 0
                        ? $"No records returned for {query}."
                        : $"Found {displayParentRecords.Count} Parent DB/device record(s), " +
                          $"{result.RelatedSiteIds.Count} related site(s), " +
                          $"{result.Tickets.Count} ticket(s), and " +
                          $"{result.SiteHistory.Count} Site History record(s).";

                if (!string.IsNullOrWhiteSpace(
                        result.Warning))
                {
                    summary +=
                        $" {result.Warning}";
                }

                StatusTextBlock.Text =
                    summary;
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Search canceled.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text =
                    $"Device Lookup failed: {ex.Message}";
            }
            finally
            {
                _searchCts?.Dispose();
                _searchCts = null;
                SearchTypeComboBox.IsEnabled = true;
                SearchButton.IsEnabled = true;
                SearchTextBox.IsEnabled = true;
                if (IsLoaded)
                    SearchTextBox.Focus();
            }
        }
    }
}
