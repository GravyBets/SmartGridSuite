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
            var state =
                _towerPingSessionState ??=
                    new TowerPingSessionState();

            foreach (var sector in _towerPingCards)
            {
                var sectorState =
                    sector.SessionState;

                if (sectorState is null ||
                    !state.Sectors.Contains(sectorState))
                {
                    sectorState =
                        state.Sectors.FirstOrDefault(x =>
                            string.Equals(
                                x.Sector,
                                sector.Sector,
                                StringComparison.OrdinalIgnoreCase));

                    if (sectorState is null)
                    {
                        sectorState =
                            new TowerSectorPingSessionState
                            {
                                Sector = sector.Sector ?? string.Empty
                            };

                        state.Sectors.Add(sectorState);
                    }

                    sector.SessionState = sectorState;
                }

                sectorState.Sector =
                    sector.Sector ?? string.Empty;

                sectorState.PingCount =
                    sector.PingCountTextBox?.Text ??
                    string.Empty;

                foreach (var endpoint in sector.Endpoints)
                {
                    var endpointState =
                        endpoint.SessionState;

                    if (endpointState is null ||
                        !sectorState.Endpoints.Contains(endpointState))
                    {
                        /*
                         * Match by label rather than IP.
                         *
                         * The technician may have manually overridden the IP,
                         * so IP A / IP B is the stable identity.
                         */
                        endpointState =
                            sectorState.Endpoints.FirstOrDefault(x =>
                                string.Equals(
                                    x.Label,
                                    endpoint.Label,
                                    StringComparison.OrdinalIgnoreCase));

                        if (endpointState is null)
                        {
                            endpointState =
                                new TowerEndpointPingSessionState
                                {
                                    Label =
                                        endpoint.Label ??
                                        string.Empty
                                };

                            sectorState.Endpoints.Add(endpointState);
                        }

                        endpoint.SessionState =
                            endpointState;
                    }

                    var currentIp =
                        (endpoint.IpTextBox?.Text ??
                         endpoint.IpAddress ??
                         string.Empty)
                        .Trim();

                    endpoint.IpAddress =
                        currentIp;

                    endpointState.Label =
                        endpoint.Label ??
                        string.Empty;

                    endpointState.IpAddress =
                        currentIp;

                    endpointState.Results =
                        endpoint.ResultTextBox?.Text ??
                        endpointState.Results;

                    endpointState.Summary =
                        NormalizeTowerSummaryForSnapshot(
                            endpoint.SummaryTextBlock?.Text);

                    endpointState.TestSuccessful =
                        endpoint.TestSuccessful;
                }
            }

            return state;
        }

        public void RestoreTowerPingSessionState(TowerPingSessionState? state)
        {
            _towerPingSessionState =
                state ??
                new TowerPingSessionState();

            foreach (var sector in _towerPingCards)
            {
                var sectorState =
                    _towerPingSessionState.Sectors
                        .FirstOrDefault(x =>
                            string.Equals(
                                x.Sector,
                                sector.Sector,
                                StringComparison.OrdinalIgnoreCase));

                if (sectorState is null)
                {
                    sectorState =
                        new TowerSectorPingSessionState
                        {
                            Sector =
                                sector.Sector ??
                                string.Empty
                        };

                    _towerPingSessionState.Sectors.Add(
                        sectorState);
                }

                sector.SessionState =
                    sectorState;

                if (sector.PingCountTextBox is not null)
                {
                    sector.PingCountTextBox.Text =
                        sectorState.PingCount ??
                        string.Empty;
                }

                foreach (var endpoint in sector.Endpoints)
                {
                    var endpointState =
                        sectorState.Endpoints
                            .FirstOrDefault(x =>
                                string.Equals(
                                    x.Label,
                                    endpoint.Label,
                                    StringComparison.OrdinalIgnoreCase));

                    if (endpointState is null)
                    {
                        endpointState =
                            new TowerEndpointPingSessionState
                            {
                                Label =
                                    endpoint.Label ??
                                    string.Empty,

                                IpAddress =
                                    endpoint.IpAddress ??
                                    string.Empty
                            };

                        sectorState.Endpoints.Add(
                            endpointState);
                    }

                    endpoint.SessionState =
                        endpointState;

                    /*
                     * Preserve a technician's manually entered IP.
                     */
                    var restoredIp =
                        (endpointState.IpAddress ??
                         string.Empty)
                        .Trim();

                    if (string.IsNullOrWhiteSpace(restoredIp))
                    {
                        restoredIp =
                            (endpoint.IpAddress ??
                             string.Empty)
                            .Trim();

                        endpointState.IpAddress =
                            restoredIp;
                    }

                    endpoint.IpAddress =
                        restoredIp;

                    if (endpoint.IpTextBox is not null &&
                        !string.Equals(
                            endpoint.IpTextBox.Text,
                            restoredIp,
                            StringComparison.Ordinal))
                    {
                        endpoint.IpTextBox.Text =
                            restoredIp;
                    }

                    if (endpoint.ResultTextBox is not null)
                    {
                        endpoint.ResultTextBox.Text =
                            endpointState.Results ??
                            string.Empty;

                        endpoint.ResultTextBox.ScrollToEnd();
                    }

                    if (endpoint.SummaryTextBlock is not null)
                    {
                        endpoint.SummaryTextBlock.Text =
                            string.IsNullOrWhiteSpace(
                                endpointState.Summary)
                                ? "Ready."
                                : endpointState.Summary;
                    }

                    endpoint.TestSuccessful =
                        endpointState.TestSuccessful;

                    if (endpointState.TestSuccessful.HasValue)
                    {
                        ApplyTowerIpStatus(
                            endpoint,
                            endpointState.TestSuccessful.Value);
                    }
                    else
                    {
                        ResetTowerIpStatus(endpoint);
                    }
                }
            }

            RefreshTowerPingButtonStates();
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