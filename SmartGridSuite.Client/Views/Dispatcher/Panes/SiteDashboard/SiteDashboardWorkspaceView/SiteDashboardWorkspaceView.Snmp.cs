using SmartGridSuite.Contracts.Snmp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private bool _syncingSnmpProfileCombo;
        private bool _syncingSnmpTargetCombo;
        private bool _syncingWritableOidCombo;
        private bool _snmpCategoryOptionsInitialized;

        private string? _snmpPrimaryIp;
        private string? _snmpLanIp;
        private string? _snmpSecondaryIp;

        private List<SnmpCategoryGroupViewModel> _snmpCategoryGroups = new();
        public event EventHandler<SnmpRunOidRequestedEventArgs>? RunSnmpOidRequested;
        public event EventHandler<SnmpRunCategoryRequestedEventArgs>? RunSnmpCategoryRequested;

        private void PollAllSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in _snmpCategoryGroups.Where(x => x.Rows.Count > 0))
            {
                var oids = group.Rows.Select(x => x.Oid).ToList();
                RunSnmpCategoryRequested?.Invoke(this, new SnmpRunCategoryRequestedEventArgs(group.Category, oids));
            }
        }

        public void ResetSnmp()
        {
            _snmpPrimaryIp = null;
            _snmpLanIp = null;
            _snmpSecondaryIp = null;
            _snmpCategoryGroups = new List<SnmpCategoryGroupViewModel>();

            _syncingSnmpProfileCombo = true;
            SnmpProfileComboBox.ItemsSource = null;
            SnmpProfileComboBox.SelectedItem = null;
            _syncingSnmpProfileCombo = false;

            _syncingWritableOidCombo = true;
            SnmpWritableOidComboBox.ItemsSource = null;
            SnmpWritableOidComboBox.SelectedItem = null;
            _syncingWritableOidCombo = false;

            SnmpCategoryItemsControl.ItemsSource = null;
            SnmpSupportInlineTextBlock.Text = "No site loaded.";

            _syncingSnmpTargetCombo = true;
            SnmpTargetComboBox.SelectedIndex = -1;
            _syncingSnmpTargetCombo = false;
            SnmpTargetTextBox.Text = string.Empty;

            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueTextBox.Visibility = Visibility.Visible;
            SnmpSetValueTextBox.IsEnabled = false;

            SnmpSetValueComboBox.ItemsSource = null;
            SnmpSetValueComboBox.SelectedItem = null;
            SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
            SnmpSetValueComboBox.IsEnabled = false;

            SetSelectedSnmpButton.IsEnabled = false;
            SnmpDecoderValuesTextBox.Text = string.Empty;
        }

        public void SetSnmpContext(bool supported, string supportMessage, string deviceFamily, string profileName, string? primaryIp, string? lanIp,
            string? secondaryIp, string? targetIp)
        {
            _snmpPrimaryIp = primaryIp;
            _snmpLanIp = lanIp;
            _snmpSecondaryIp = secondaryIp;

            SnmpSupportInlineTextBlock.Text = string.IsNullOrWhiteSpace(supportMessage)
                ? "—"
                : supportMessage;

            var targets = new List<SnmpTargetChoice>();

            AddSnmpTargetChoice(targets, "Primary", "Primary IP", primaryIp);
            AddSnmpTargetChoice(targets, "LAN", "LAN IP", lanIp);
            AddSnmpTargetChoice(targets, "Secondary", "Secondary IP", secondaryIp);

            ApplySnmpTargetChoices(targets, targetIp);
        }

        public void SetSnmpTargetOptions(IEnumerable<(string Key, string Label, string IpAddress)> targets, string? targetIp)
        {
            var list = new List<SnmpTargetChoice>();

            foreach (var target in targets)
            {
                AddSnmpTargetChoice(
                    list,
                    target.Key,
                    target.Label,
                    target.IpAddress);
            }

            ApplySnmpTargetChoices(list, targetIp);
        }

        private void ApplySnmpTargetChoices(List<SnmpTargetChoice> targets, string? targetIp)
        {
            var cleanTargetIp = (targetIp ?? string.Empty).Trim();

            _syncingSnmpTargetCombo = true;

            SnmpTargetComboBox.ItemsSource = null;
            SnmpTargetComboBox.DisplayMemberPath = nameof(SnmpTargetChoice.DisplayLabel);
            SnmpTargetComboBox.SelectedValuePath = nameof(SnmpTargetChoice.Key);
            SnmpTargetComboBox.ItemsSource = targets;

            SnmpTargetChoice? selected = null;

            if (!string.IsNullOrWhiteSpace(cleanTargetIp))
            {
                selected = targets.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.IpAddress) &&
                    string.Equals(x.IpAddress, cleanTargetIp, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                selected = targets.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.IpAddress));
            }

            SnmpTargetComboBox.SelectedItem = selected;

            _syncingSnmpTargetCombo = false;

            SnmpTargetTextBox.Text = !string.IsNullOrWhiteSpace(cleanTargetIp)
                ? cleanTargetIp
                : selected?.IpAddress ?? string.Empty;
        }

        public void SetSnmpProfiles(IEnumerable<SnmpProfileListItemDto> profiles, ulong? selectedProfileId)
        {
            _syncingSnmpProfileCombo = true;

            var list = (profiles ?? Enumerable.Empty<SnmpProfileListItemDto>())
                .Select(x => new SnmpProfileChoice
                {
                    Id = x.Id,
                    DisplayLabel = x.Name
                })
                .ToList();

            SnmpProfileComboBox.ItemsSource = list;

            if (selectedProfileId.HasValue)
                SnmpProfileComboBox.SelectedValue = selectedProfileId.Value;
            else
                SnmpProfileComboBox.SelectedItem = null;

            _syncingSnmpProfileCombo = false;
        }

        public void SetSnmpOids(IEnumerable<SnmpOidConfigDto> oids, IDictionary<ulong, string>? resultMap = null)
        {
            var list = oids?.ToList() ?? new List<SnmpOidConfigDto>();

            _snmpCategoryGroups = BuildSnmpCategoryGroups(list, resultMap);
            SnmpCategoryItemsControl.ItemsSource = _snmpCategoryGroups;

            _syncingWritableOidCombo = true;

            var writable = list
                .Where(x => x.IsWritable)
                .OrderBy(x => x.Label)
                .Select(x => new SnmpWritableOidChoice
                {
                    DisplayLabel = x.Label,
                    Oid = x
                })
                .ToList();

            SnmpWritableOidComboBox.ItemsSource = writable;
            SnmpWritableOidComboBox.SelectedItem = null;

            _syncingWritableOidCombo = false;

            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueTextBox.Visibility = Visibility.Visible;
            SnmpSetValueTextBox.IsEnabled = false;

            SnmpSetValueComboBox.ItemsSource = null;
            SnmpSetValueComboBox.SelectedItem = null;
            SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
            SnmpSetValueComboBox.IsEnabled = false;

            SetSelectedSnmpButton.IsEnabled = false;
            SnmpDecoderValuesTextBox.Text = string.Empty;
        }

        private void SnmpTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSnmpTargetCombo)
                return;

            if (SnmpTargetComboBox.SelectedItem is SnmpTargetChoice choice)
                SnmpTargetTextBox.Text = choice.IpAddress ?? string.Empty;

            SnmpTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        public ulong? GetSelectedSnmpProfileId()
        {
            if (SnmpProfileComboBox.SelectedValue is ulong id)
                return id;

            if (SnmpProfileComboBox.SelectedItem is SnmpProfileChoice choice)
                return choice.Id;

            return null;
        }

        public SnmpOidConfigDto? GetSelectedWritableSnmpOid()
        {
            return (SnmpWritableOidComboBox.SelectedItem as SnmpWritableOidChoice)?.Oid;
        }

        public string GetSnmpTargetIp()
        {
            return (SnmpTargetTextBox.Text ?? string.Empty).Trim();
        }

        public string GetSnmpSetValue()
        {
            if (SnmpSetValueComboBox.Visibility == Visibility.Visible)
            {
                if (SnmpSetValueComboBox.SelectedValue is string raw)
                    return raw.Trim();

                if (SnmpSetValueComboBox.SelectedItem is SnmpSetValueChoice choice)
                    return choice.RawValue.Trim();

                return string.Empty;
            }

            return (SnmpSetValueTextBox.Text ?? string.Empty).Trim();
        }

        public void SetSnmpOidResult(ulong oidId, string resultText)
        {
            foreach (var row in _snmpCategoryGroups.SelectMany(x => x.Rows))
            {
                if (row.Id == oidId)
                {
                    row.ResultText = resultText;
                    return;
                }
            }
        }

        private List<SnmpCategoryGroupViewModel> BuildSnmpCategoryGroups(IReadOnlyCollection<SnmpOidConfigDto> oids, IDictionary<ulong, string>? resultMap)
        {
            var categoryOrder = new[] { "Admin", "Config", "Stats" };
            var groups = new List<SnmpCategoryGroupViewModel>();

            foreach (var category in categoryOrder)
            {
                var rows = oids
                    .Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Label)
                    .Select(x => new SnmpOidRowViewModel
                    {
                        Oid = x,
                        ResultText = resultMap is not null && resultMap.TryGetValue(x.Id, out var result)
                            ? result
                            : string.Empty
                    })
                    .ToList();

                groups.Add(new SnmpCategoryGroupViewModel
                {
                    Category = category,
                    Rows = new ObservableCollection<SnmpOidRowViewModel>(rows)
                });
            }

            return groups;
        }

        private void RefreshSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshSnmpRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SetSelectedSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedSnmpRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SnmpTargetTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SnmpTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SnmpProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSnmpProfileCombo)
                return;

            SelectedSnmpProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SnmpWritableOidComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingWritableOidCombo)
                return;

            var choice = SnmpWritableOidComboBox.SelectedItem as SnmpWritableOidChoice;
            UpdateWritableSnmpUi(choice?.Oid);
        }

        private void RunSnmpOidButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is SnmpOidRowViewModel row)
                RunSnmpOidRequested?.Invoke(this, new SnmpRunOidRequestedEventArgs(row.Oid));
        }

        private static void AddSnmpTargetChoice(ICollection<SnmpTargetChoice> items, string key, string label, string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            var trimmed = ip.Trim();

            items.Add(new SnmpTargetChoice
            {
                Key = key,
                IpAddress = trimmed,
                DisplayLabel = label
            });
        }

        private void UpdateWritableSnmpUi(SnmpOidConfigDto? oid)
        {
            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueComboBox.ItemsSource = null;
            SnmpSetValueComboBox.SelectedItem = null;

            if (oid is null)
            {
                SnmpSetValueTextBox.Visibility = Visibility.Visible;
                SnmpSetValueTextBox.IsEnabled = false;
                SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
                SnmpSetValueComboBox.IsEnabled = false;
                SetSelectedSnmpButton.IsEnabled = false;
                SnmpDecoderValuesTextBox.Text = string.Empty;
                return;
            }

            if (!oid.IsWritable)
            {
                SnmpSetValueTextBox.Visibility = Visibility.Visible;
                SnmpSetValueTextBox.IsEnabled = false;
                SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
                SnmpSetValueComboBox.IsEnabled = false;
                SetSelectedSnmpButton.IsEnabled = false;
                SnmpDecoderValuesTextBox.Text = "Selected OID is read-only.";
                return;
            }

            SetSelectedSnmpButton.IsEnabled = true;

            if (oid.DecodeValues is { Count: > 0 })
            {
                var decodeChoices = oid.DecodeValues
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.RawValue)
                    .Select(x => new SnmpSetValueChoice
                    {
                        RawValue = x.RawValue,
                        DisplayLabel = $"{x.RawValue} = {x.DisplayText}"
                    })
                    .ToList();

                SnmpSetValueTextBox.Visibility = Visibility.Collapsed;
                SnmpSetValueTextBox.IsEnabled = false;

                SnmpSetValueComboBox.ItemsSource = decodeChoices;
                SnmpSetValueComboBox.Visibility = Visibility.Visible;
                SnmpSetValueComboBox.IsEnabled = true;
                SnmpSetValueComboBox.SelectedIndex = 0;

                SnmpDecoderValuesTextBox.Text =
                    "Decoder Values:" + Environment.NewLine +
                    string.Join(Environment.NewLine, decodeChoices.Select(x => x.DisplayLabel));
            }
            else
            {
                SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
                SnmpSetValueComboBox.IsEnabled = false;

                SnmpSetValueTextBox.Visibility = Visibility.Visible;
                SnmpSetValueTextBox.IsEnabled = true;

                SnmpDecoderValuesTextBox.Text = "No decoder values configured for this OID. Enter the raw value manually.";
            }
        }

        public void ShowSnmpSetResult(SnmpSetResultDto? result)
        {
            if (result is null)
            {
                SnmpDecoderValuesTextBox.Text = "No SNMP set result.";
                return;
            }

            SnmpDecoderValuesTextBox.Text = result.Success
                ? $"Set succeeded for {result.Label}:{Environment.NewLine}{result.DisplayValue}"
                : $"Set failed for {result.Label}:{Environment.NewLine}{result.ErrorMessage}";
        }

        public void ShowSnmpSetAndRefreshResult(SnmpSetResultDto? setResult, SnmpRunResultDto? refreshResult)
        {
            if (setResult is null)
            {
                SnmpDecoderValuesTextBox.Text = "No SNMP set result.";
                return;
            }

            var setText = setResult.Success
                ? $"Set succeeded for {setResult.Label}:{Environment.NewLine}{setResult.DisplayValue}"
                : $"Set failed for {setResult.Label}:{Environment.NewLine}{setResult.ErrorMessage}";

            if (setResult.Success != true)
            {
                SnmpDecoderValuesTextBox.Text = setText;
                return;
            }

            var refreshText = refreshResult?.Success == true
                ? $"Refreshed selected value:{Environment.NewLine}{refreshResult.DisplayValue}"
                : $"Refresh failed:{Environment.NewLine}{refreshResult?.ErrorMessage ?? "No SNMP refresh result returned."}";

            SnmpDecoderValuesTextBox.Text =
                setText +
                Environment.NewLine +
                Environment.NewLine +
                refreshText;
        }

        private static bool IsUsefulSnmpResultText(string? rawValue)
        {
            var value = (rawValue ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Equals("—", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Ready.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Running...", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Polling...", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("No data", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("No value", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Not polled", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Not polled.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (value.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("ERROR ", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("SNMP not supported", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("No active SNMP profile", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string NormalizeSnmpResultForWriteUp(string? rawValue)
        {
            var value = (rawValue ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var parts = value
                .Split(new[] { "\r\n", "\n", "\r", "\t" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var normalized = parts.Count == 0
                ? value
                : string.Join(" | ", parts);

            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ");

            return normalized.Trim();
        }

        //SNMP Helpers
        private sealed class SnmpProfileChoice
        {
            public ulong Id { get; set; }
            public string DisplayLabel { get; set; } = "";

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpWritableOidChoice
        {
            public string DisplayLabel { get; set; } = "";
            public SnmpOidConfigDto Oid { get; set; } = new();

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpTargetChoice
        {
            public string Key { get; set; } = "";
            public string DisplayLabel { get; set; } = "";
            public string IpAddress { get; set; } = "";

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpSetValueChoice
        {
            public string RawValue { get; set; } = "";
            public string DisplayLabel { get; set; } = "";

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpOidRowViewModel : INotifyPropertyChanged
        {
            private string _resultText = string.Empty;

            public SnmpOidConfigDto Oid { get; set; } = new();

            public ulong Id => Oid.Id;
            public string Label => Oid.Label;

            public string ResultText
            {
                get => _resultText;
                set
                {
                    if (_resultText == value)
                        return;

                    _resultText = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private sealed class SnmpCategoryGroupViewModel
        {
            public string Category { get; set; } = "";
            public ObservableCollection<SnmpOidRowViewModel> Rows { get; set; } = new();

            public string EmptyMessage => Rows.Count == 0 ? "No OIDs configured." : string.Empty;
            public Visibility EmptyMessageVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}