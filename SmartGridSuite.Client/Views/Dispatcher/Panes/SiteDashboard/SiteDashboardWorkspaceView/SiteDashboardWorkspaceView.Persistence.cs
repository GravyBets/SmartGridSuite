using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        public List<EquipmentReplacementSessionEntry> GetEquipmentReplacementSessionEntries()
        {
            var entries = new List<EquipmentReplacementSessionEntry>();

            if (ReplacementEntriesPanel is null)
                return entries;

            foreach (var child in ReplacementEntriesPanel.Children)
            {
                if (child is not Border rowBorder)
                    continue;

                if (rowBorder.Tag is not ReplacementEntryRowTag rowTag)
                    continue;

                var entry = GetEquipmentReplacementEntry(rowBorder, rowTag);

                var isBlank =
                    string.IsNullOrWhiteSpace(entry.Item) &&
                    string.IsNullOrWhiteSpace(entry.OldSerial) &&
                    string.IsNullOrWhiteSpace(entry.NewSerial);

                if (isBlank)
                    continue;

                entries.Add(new EquipmentReplacementSessionEntry
                {
                    SlotLabel = rowTag.Label,
                    UsesCommunicationDeviceTypePicker = rowTag.UsesCommunicationDeviceTypePicker,
                    ReplacementKey = rowTag.ReplacementKey ?? string.Empty,
                    Item = entry.Item,
                    OldSerial = entry.OldSerial,
                    NewSerial = entry.NewSerial
                });
            }

            return entries;
        }

        public void RestoreEquipmentReplacementSessionEntries(IEnumerable<EquipmentReplacementSessionEntry>? entries)
        {
            if (entries is null || ReplacementEntriesPanel is null)
                return;

            var restoredAnySwapKey = false;

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Item) &&
                    string.IsNullOrWhiteSpace(entry.OldSerial) &&
                    string.IsNullOrWhiteSpace(entry.NewSerial))
                {
                    continue;
                }

                var replacementKey = entry.ReplacementKey;

                // Fallback for entries saved before ReplacementKey existed.
                if (string.IsNullOrWhiteSpace(replacementKey) &&
                    !string.IsNullOrWhiteSpace(entry.SlotLabel))
                {
                    replacementKey = BuildReplacementEntryKey(entry.SlotLabel, entry.OldSerial);
                }

                if (!string.IsNullOrWhiteSpace(replacementKey))
                {
                    _activeReplacementEntryKeys.Add(replacementKey);
                    restoredAnySwapKey = true;
                }

                AddReplacementEntryRow(
                    label: entry.SlotLabel,
                    oldSerial: entry.OldSerial,
                    allowCustomLabel: true,
                    usesCommunicationDeviceTypePicker: entry.UsesCommunicationDeviceTypePicker,
                    replacementKey: string.IsNullOrWhiteSpace(replacementKey) ? null : replacementKey);

                if (ReplacementEntriesPanel.Children.Count == 0)
                    continue;

                if (ReplacementEntriesPanel.Children[^1] is not Border rowBorder)
                    continue;

                SetTaggedTextBoxValue(rowBorder, "ReplacementItem", entry.Item);
                SetTaggedTextBoxValue(rowBorder, "ReplacementOldSerial", entry.OldSerial);
                SetTaggedTextBoxValue(rowBorder, "ReplacementNewSerial", entry.NewSerial);
                SetTaggedComboBoxValue(rowBorder, "ReplacementDeviceType", entry.Item);
            }

            // Important: rebuild serialized equipment cards AFTER restoring keys,
            // so Swap buttons visually disable/gray out correctly.
            if (restoredAnySwapKey)
                RefreshEquipmentCards();
        }

        private static void SetTaggedTextBoxValue(DependencyObject root, string tag, string? value)
        {
            var textBox = FindVisualChildren<TextBox>(root)
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

            if (textBox is not null)
                textBox.Text = value ?? string.Empty;
        }

        private static void SetTaggedComboBoxValue(DependencyObject root, string tag, string? value)
        {
            var comboBox = FindVisualChildren<ComboBox>(root)
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

            if (comboBox is null)
                return;

            var cleanValue = (value ?? string.Empty).Trim();

            foreach (var item in comboBox.Items)
            {
                var itemText = item switch
                {
                    ComboBoxItem comboBoxItem => comboBoxItem.Content?.ToString(),
                    _ => item?.ToString()
                };

                if (string.Equals(itemText?.Trim(), cleanValue, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.Text = cleanValue;
        }

        public Dictionary<ulong, string> GetSnmpOidResultSnapshot()
        {
            return _snmpCategoryGroups
                .SelectMany(x => x.Rows)
                .Where(x => x.Id > 0)
                .ToDictionary(
                    x => x.Id,
                    x => x.ResultText ?? string.Empty);
        }

        public TowerPingSessionState GetTowerPingSessionState()
        {
            var state = new TowerPingSessionState();

            foreach (var sector in _towerPingCards)
            {
                var sectorState = new TowerSectorPingSessionState
                {
                    Sector = sector.Sector ?? string.Empty,
                    PingCount = sector.PingCountTextBox?.Text ?? string.Empty
                };

                foreach (var endpoint in sector.Endpoints)
                {
                    sectorState.Endpoints.Add(new TowerEndpointPingSessionState
                    {
                        Label = endpoint.Label ?? string.Empty,
                        IpAddress = endpoint.IpAddress ?? string.Empty,
                        Results = endpoint.ResultTextBox?.Text ?? string.Empty,
                        Summary = NormalizeTowerSummaryForSnapshot(endpoint.SummaryTextBlock?.Text),
                        TestSuccessful = endpoint.TestSuccessful
                    });
                }

                if (!string.IsNullOrWhiteSpace(sectorState.PingCount) ||
                    sectorState.Endpoints.Any(x =>
                        !string.IsNullOrWhiteSpace(x.Results) ||
                        !string.IsNullOrWhiteSpace(x.Summary) && !x.Summary.Equals("Ready.", StringComparison.OrdinalIgnoreCase) ||
                        x.TestSuccessful.HasValue))
                {
                    state.Sectors.Add(sectorState);
                }
            }

            return state;
        }

        public void RestoreTowerPingSessionState(TowerPingSessionState? state)
        {
            if (state is null || state.Sectors.Count == 0)
                return;

            foreach (var sectorState in state.Sectors)
            {
                var sector = _towerPingCards.FirstOrDefault(x =>
                    string.Equals(x.Sector, sectorState.Sector, StringComparison.OrdinalIgnoreCase));

                if (sector is null)
                    continue;

                if (sector.PingCountTextBox is not null)
                    sector.PingCountTextBox.Text = sectorState.PingCount ?? string.Empty;

                foreach (var endpointState in sectorState.Endpoints)
                {
                    var endpoint = sector.Endpoints.FirstOrDefault(x =>
                        string.Equals(x.Label, endpointState.Label, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.IpAddress, endpointState.IpAddress, StringComparison.OrdinalIgnoreCase));

                    if (endpoint is null)
                    {
                        endpoint = sector.Endpoints.FirstOrDefault(x =>
                            string.Equals(x.Label, endpointState.Label, StringComparison.OrdinalIgnoreCase));
                    }

                    if (endpoint is null)
                        continue;

                    if (endpoint.ResultTextBox is not null)
                    {
                        endpoint.ResultTextBox.Text = endpointState.Results ?? string.Empty;
                        endpoint.ResultTextBox.ScrollToEnd();
                    }

                    if (endpoint.SummaryTextBlock is not null)
                    {
                        endpoint.SummaryTextBlock.Text = string.IsNullOrWhiteSpace(endpointState.Summary)
                            ? "Ready."
                            : endpointState.Summary;
                    }

                    endpoint.TestSuccessful = endpointState.TestSuccessful;

                    if (endpointState.TestSuccessful.HasValue)
                        ApplyTowerIpStatus(endpoint, endpointState.TestSuccessful.Value);
                    else
                        ResetTowerIpStatus(endpoint);
                }
            }
        }

        private static string NormalizeTowerSummaryForSnapshot(string? summary)
        {
            var text = (summary ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "Ready.";

            if (text.Equals("Testing...", StringComparison.OrdinalIgnoreCase))
                return "Ready.";

            return text.Replace(" • Running...", string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}