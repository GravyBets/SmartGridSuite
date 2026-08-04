using SmartGridSuite.Contracts.Settings;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private int _serializedDeviceSectionCount;
        private readonly HashSet<string> _activeReplacementEntryKeys = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxReplacementEntries = 15;
        private bool _showSensitiveEquipmentValues;
        private static readonly string[] FallbackCommunicationDeviceTypes =
        {
            "Radio",
            "PMR",
            "LTE Modem",
            "Cell Modem",
            "AP",
            "Router",
            "Other"
        };

        private List<CommunicationDeviceTypeDto> _communicationDeviceTypes = new();
        private string _equipmentText = string.Empty;

        private sealed class SerializedDeviceInfo
        {
            public string Label { get; set; } = string.Empty;
            public string OldSerial { get; set; } = string.Empty;
            public string ReplacementKey { get; set; } = string.Empty;
            public bool UsesCommunicationDeviceTypePicker { get; set; }
        }

        private sealed class ReplacementEntryRowTag
        {
            public string Label { get; set; } = string.Empty;
            public bool UsesCommunicationDeviceTypePicker { get; set; }
            public string? ReplacementKey { get; set; }
        }

        private sealed class EquipmentReplacementWriteUpEntry
        {
            public string SlotLabel { get; set; } = string.Empty;
            public string Item { get; set; } = string.Empty;
            public string OldSerial { get; set; } = string.Empty;
            public string NewSerial { get; set; } = string.Empty;
            public bool UsesCommunicationDeviceTypePicker { get; set; }
        }

        public void RefreshEquipmentDisplay()
        {
            RefreshEquipmentCards();
        }

        private void RefreshEquipmentCards()
        {
            if (SerializedDevicesPanel is null || AccessSecuritySectionPanel is null)
                return;

            SerializedDevicesPanel.Children.Clear();
            AccessSecuritySectionPanel.Children.Clear();
            _serializedDeviceSectionCount = 0;

            var isIgsd = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Igsd,
                StringComparison.OrdinalIgnoreCase);

            var isAmsMr = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.AmsMr,
                StringComparison.OrdinalIgnoreCase);

            var isDacs = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Dacs,
                StringComparison.OrdinalIgnoreCase);

            var isRx = IsRangeExtenderDashboard;

            if (AccessSecurityCard is not null)
                AccessSecurityCard.Visibility = (isRx || isDacs) ? Visibility.Collapsed : Visibility.Visible;

            if (isRx)
            {
                AddSerializedDeviceSection(
                    title: "Range Extender",
                    model: null,
                    serial: GetEquipmentValue("Range Extender SN", "Meter Number"),
                    swapLabel: "Range Extender");

                return;
            }

            if (isDacs)
            {
                AddSerializedDeviceSection(
                    title: "Primary Communications",
                    model: null,
                    serial: GetEquipmentValue("Primary SN", "Primary Communications SN", "Radio SN"),
                    swapLabel: "Primary Communications",
                    usesCommunicationDeviceTypePicker: true);

                AddSerializedDeviceSection(
                    title: "Antenna",
                    model: null,
                    serial: GetEquipmentValue("Antenna SN"),
                    swapLabel: "Antenna");

                return;
            }

            AddSerializedDeviceSection(
                title: "Enclosure",
                model: GetEquipmentValue("Enclosure Model"),
                serial: GetEquipmentValue("Enclosure SN"),
                swapLabel: "Enclosure",
                showModelBesideSerial: true);

            AddSerializedDeviceSection(
                title: "Primary Communications",
                model: null,
                serial: GetEquipmentValue("Primary SN"),
                swapLabel: "Primary Communications",
                usesCommunicationDeviceTypePicker: true);

            AddSerializedDeviceSection(
                title: "Secondary Communications",
                model: null,
                serial: GetEquipmentValue("Secondary SN"),
                swapLabel: "Secondary Communications",
                usesCommunicationDeviceTypePicker: true);

            AddSerializedDeviceSection(
                title: "Antenna",
                model: null,
                serial: GetEquipmentValue("Antenna SN"),
                swapLabel: "Antenna");

            if (isIgsd)
            {
                AddSerializedDeviceSection(
                    title: "Cyberlock",
                    model: null,
                    serial: GetEquipmentValue("Cyberlock SN"),
                    swapLabel: "Cyberlock");
            }

            var hasSensitiveRows = false;

            if (isIgsd)
            {
                hasSensitiveRows |= AddSensitiveEquipmentRow(
                    "Tunnel PSK",
                    GetEquipmentValue("Tunnel PSK"));
            }

            if (isAmsMr)
            {
                hasSensitiveRows |= AddSensitiveEquipmentRow(
                    "Secondary WiFi SSID",
                    GetEquipmentValue("Secondary WiFi SSID", "Secondary SSID"));

                hasSensitiveRows |= AddSensitiveEquipmentRow(
                    "Secondary WiFi Password",
                    GetEquipmentValue("Secondary WiFi Password", "Secondary Password"));
            }

            if (!hasSensitiveRows)
            {
                AccessSecuritySectionPanel.Children.Add(new TextBlock
                {
                    Text = "No data",
                    FontStyle = FontStyles.Italic,
                    Foreground = TryFindResource("TextSecondary") as Brush
                });
            }
        }

        private void AddSerializedDeviceSection(string title, string? model, string? serial, string swapLabel,
            bool showModelBesideSerial = false, bool usesCommunicationDeviceTypePicker = false)
        {
            if (_serializedDeviceSectionCount > 0)
                SerializedDevicesPanel.Children.Add(CreateSerializedDeviceSeparator());

            var oldSerial = string.IsNullOrWhiteSpace(serial)
                ? string.Empty
                : serial.Trim();

            var replacementKey = BuildReplacementEntryKey(swapLabel, oldSerial);
            var replacementAlreadyAdded = _activeReplacementEntryKeys.Contains(replacementKey);

            var section = new StackPanel();

            var headerGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(titleBlock, 0);

            var swapButton = new Button
            {
                Content = CreateSwapButtonContent(),
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 26,
                MinWidth = 82,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !replacementAlreadyAdded,
                ToolTip = replacementAlreadyAdded
                    ? "A replacement entry already exists for this device."
                    : "Create replacement entry",
                Tag = new SerializedDeviceInfo
                {
                    Label = swapLabel,
                    OldSerial = oldSerial,
                    ReplacementKey = replacementKey,
                    UsesCommunicationDeviceTypePicker = usesCommunicationDeviceTypePicker
                }
            };

            swapButton.Click += SwapSerializedDeviceButton_Click;
            Grid.SetColumn(swapButton, 1);

            headerGrid.Children.Add(titleBlock);
            headerGrid.Children.Add(swapButton);

            section.Children.Add(headerGrid);

            if (showModelBesideSerial)
            {
                section.Children.Add(CreateSideBySideEquipmentValues(
                    "Model",
                    string.IsNullOrWhiteSpace(model) ? "No data" : model.Trim(),
                    "Serial Number",
                    string.IsNullOrWhiteSpace(oldSerial) ? "No data" : oldSerial));
            }
            else
            {
                section.Children.Add(CreateStackedEquipmentValue(
                    "Serial Number",
                    string.IsNullOrWhiteSpace(oldSerial)
                        ? "Not returned by database"
                        : oldSerial));
            }

            SerializedDevicesPanel.Children.Add(section);
            _serializedDeviceSectionCount++;
        }

        private FrameworkElement CreateSerializedDeviceSeparator()
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(0, 10, 0, 10),
                Background = TryFindResource("SurfaceBorder") as Brush
            };
        }

        private FrameworkElement CreateSideBySideEquipmentValues(string leftLabel, string leftValue, string rightLabel, string rightValue)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 2)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = CreateValueStack(leftLabel, leftValue);
            Grid.SetColumn(left, 0);

            var right = CreateValueStack(rightLabel, rightValue);
            Grid.SetColumn(right, 2);

            grid.Children.Add(left);
            grid.Children.Add(right);

            return grid;
        }

        private FrameworkElement CreateStackedEquipmentValue(string label, string value)
        {
            return CreateValueStack(label, value, new Thickness(0, 0, 0, 2));
        }

        private bool AddSensitiveEquipmentRow(string label, string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            var cleanValue = rawValue.Trim();

            var displayValue = _showSensitiveEquipmentValues
                ? cleanValue
                : MaskSensitiveValue(cleanValue);

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(0, 0, 0, 8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            var valuePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 0)
            };

            valuePanel.Children.Add(new TextBlock
            {
                Text = displayValue,
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            });

            var copyIcon = CreateTinyInlineCopyIcon($"Copy {label}");
            var copyVisualVersion = 0;

            copyIcon.MouseLeftButtonUp += async (_, _) =>
            {
                var copied = await TryCopyToClipboardAsync(cleanValue);

                if (!copied)
                {
                    copyIcon.ToolTip = "Could not copy. Try again.";
                    return;
                }

                var thisVersion = ++copyVisualVersion;

                copyIcon.Text = CheckGlyph;
                copyIcon.ToolTip = "Copied!";

                await Task.Delay(TimeSpan.FromSeconds(3));

                if (copyVisualVersion == thisVersion)
                {
                    copyIcon.Text = CopyGlyph;
                    copyIcon.ToolTip = $"Copy {label}";
                }
            };

            valuePanel.Children.Add(copyIcon);
            stack.Children.Add(valuePanel);

            border.Child = stack;
            AccessSecuritySectionPanel.Children.Add(border);

            return true;
        }

        private void AddReplacementEntryRow(string? label = null, string? oldSerial = null, bool allowCustomLabel = true, bool usesCommunicationDeviceTypePicker = false,
            string? replacementKey = null)
        {
            if (ReplacementEntriesPanel is null)
                return;

            if (!CanAddReplacementEntry())
                return;

            var cleanLabel = (label ?? string.Empty).Trim();
            var cleanOldSerial = (oldSerial ?? string.Empty).Trim();

            var outerBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SurfaceBg") as Brush
            };

            outerBorder.Tag = new ReplacementEntryRowTag
            {
                Label = cleanLabel,
                UsesCommunicationDeviceTypePicker = usesCommunicationDeviceTypePicker,
                ReplacementKey = replacementKey
            };

            var root = new Grid();

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = usesCommunicationDeviceTypePicker && !string.IsNullOrWhiteSpace(cleanLabel)
                ? $"{cleanLabel} Replacement"
                : "Replacement Entry";

            var titleBlock = new TextBlock
            {
                Text = headerText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(titleBlock, 0);

            var removeButton = new Button
            {
                Content = CreateTrashButtonContent(),
                Height = 30,
                Width = 30,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Remove entry",
                Cursor = Cursors.Hand
            };

            removeButton.MouseEnter += (_, _) =>
            {
                removeButton.Background =
                    TryFindResource("DangerBgHover") as Brush
                    ?? new SolidColorBrush(
                        Color.FromArgb(
                            24,
                            220,
                            80,
                            80));

                if (removeButton.Content is Path icon)
                {
                    icon.Stroke =
                        TryFindResource("DangerText") as Brush
                        ?? new SolidColorBrush(
                            Color.FromRgb(
                                220,
                                80,
                                80));
                }
            };

            removeButton.MouseLeave += (_, _) =>
            {
                removeButton.Background =
                    Brushes.Transparent;

                if (removeButton.Content is Path icon)
                {
                    icon.Stroke =
                        TryFindResource("TextSecondary") as Brush;
                }
            };

            removeButton.Click += (_, _) =>
            {
                ReplacementEntriesPanel.Children.Remove(outerBorder);

                if (!string.IsNullOrWhiteSpace(replacementKey))
                {
                    _activeReplacementEntryKeys.Remove(replacementKey);
                    RefreshEquipmentCards();
                }
            };

            Grid.SetColumn(removeButton, 1);

            headerGrid.Children.Add(titleBlock);
            headerGrid.Children.Add(removeButton);

            Grid.SetRow(headerGrid, 0);
            root.Children.Add(headerGrid);

            // Fields
            var fieldsGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            /*
             * Swap rows:
             * Device Type, Old Serial, and New Serial share the width evenly.
             *
             * Blank/manual rows:
             * Item is slightly narrower, giving Old/New Serial more room.
             *
             * Every column remains proportional, so all three controls shrink
             * together on laptop screens instead of being cut off.
             */
            var firstFieldWeight =
                usesCommunicationDeviceTypePicker
                    ? 1.0
                    : 0.75;

            var serialFieldWeight =
                usesCommunicationDeviceTypePicker
                    ? 1.0
                    : 1.125;

            fieldsGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        firstFieldWeight,
                        GridUnitType.Star)
                });

            fieldsGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(8)
                });

            fieldsGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        serialFieldWeight,
                        GridUnitType.Star)
                });

            fieldsGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(8)
                });

            fieldsGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        serialFieldWeight,
                        GridUnitType.Star)
                });

            var firstField = usesCommunicationDeviceTypePicker
                ? CreateCommunicationDeviceTypePicker("Device Type")
                : CreateReplacementField("Item", cleanLabel, isReadOnly: !allowCustomLabel, fieldKey: "ReplacementItem");

            Grid.SetColumn(firstField, 0);

            var oldSerialField = CreateReplacementField(
                "Old Serial",
                cleanOldSerial,
                isReadOnly: false,
                fieldKey: "ReplacementOldSerial");

            Grid.SetColumn(oldSerialField, 2);

            var newSerialField = CreateReplacementField(
                "New Serial",
                string.Empty,
                isReadOnly: false,
                fieldKey: "ReplacementNewSerial");

            Grid.SetColumn(newSerialField, 4);

            fieldsGrid.Children.Add(firstField);
            fieldsGrid.Children.Add(oldSerialField);
            fieldsGrid.Children.Add(newSerialField);

            Grid.SetRow(fieldsGrid, 2);
            root.Children.Add(fieldsGrid);

            outerBorder.Child = root;
            ReplacementEntriesPanel.Children.Add(outerBorder);
        }

        private FrameworkElement CreateReplacementField(
            string label,
            string value,
            bool isReadOnly,
            string? fieldKey = null)
        {
            var stack = new StackPanel
            {
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            stack.Children.Add(
                new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground =
                        TryFindResource("TextSecondary") as Brush,
                    Margin = new Thickness(0, 0, 0, 4)
                });

            var textBox = new TextBox
            {
                Text = value,
                Tag = fieldKey,
                IsReadOnly = isReadOnly,
                Style = (Style)FindResource("ModernWatermarkTextBox"),
                Height = 30,
                Width = double.NaN,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (string.Equals(
                    fieldKey,
                    "ReplacementItem",
                    StringComparison.Ordinal))
            {
                textBox.TextChanged += (_, _) =>
                {
                    ClearRequiredFieldWarning(textBox);
                };
            }

            stack.Children.Add(textBox);

            return stack;
        }

        private FrameworkElement CreateCommunicationDeviceTypePicker(string label)
        {
            var stack = new StackPanel
            {
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            stack.Children.Add(
                new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground =
                        TryFindResource("TextSecondary") as Brush,
                    Margin = new Thickness(0, 0, 0, 4)
                });

            var comboBox = new ComboBox
            {
                Height = 30,

                /*
                 * Force the ComboBox to fill its entire proportional
                 * Grid column instead of retaining the narrow width
                 * supplied by the shared style.
                 */
                Width = double.NaN,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                IsEditable = false,
                Tag = "ReplacementDeviceType",

                ToolTip =
                    "Select a device type before submitting this replacement."
            };

            if (TryFindResource("ModernComboBoxStyle") is Style comboStyle)
            {
                comboBox.Style = comboStyle;
            }

            var names = _communicationDeviceTypes
                .Where(
                    x =>
                        x.IsActive &&
                        !string.IsNullOrWhiteSpace(
                            x.DisplayName))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .Select(x => x.DisplayName.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                names =
                    FallbackCommunicationDeviceTypes
                        .ToList();
            }

            foreach (var name in names)
            {
                comboBox.Items.Add(name);
            }

            // Required selection remains blank until the user chooses.
            comboBox.SelectedIndex = -1;

            comboBox.SelectionChanged += (_, _) =>
            {
                ClearRequiredFieldWarning(comboBox);
            };

            stack.Children.Add(comboBox);

            return stack;
        }

        private void SwapSerializedDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SerializedDeviceInfo info)
                return;

            if (!CanAddReplacementEntry())
                return;

            var replacementKey = string.IsNullOrWhiteSpace(info.ReplacementKey)
                ? BuildReplacementEntryKey(info.Label, info.OldSerial)
                : info.ReplacementKey;

            if (_activeReplacementEntryKeys.Contains(replacementKey))
                return;

            _activeReplacementEntryKeys.Add(replacementKey);

            button.IsEnabled = false;
            button.ToolTip = "A replacement entry already exists for this device.";
            button.Content = CreateAddedReplacementButtonContent();

            AddReplacementEntryRow(
                label: info.Label,
                oldSerial: info.OldSerial,
                allowCustomLabel: false,
                usesCommunicationDeviceTypePicker: info.UsesCommunicationDeviceTypePicker,
                replacementKey: replacementKey);
        }

        private void AddReplacementEntryButton_Click(object sender, RoutedEventArgs e)
        {
            AddReplacementEntryRow(
                label: string.Empty,
                oldSerial: string.Empty,
                allowCustomLabel: true);
        }

        private void ToggleSensitiveEquipmentButton_Click(object sender, RoutedEventArgs e)
        {
            _showSensitiveEquipmentValues = !_showSensitiveEquipmentValues;

            if (ToggleSensitiveEquipmentButton is not null)
                ToggleSensitiveEquipmentButton.Content = _showSensitiveEquipmentValues ? "Hide" : "View";

            RefreshEquipmentCards();
        }

        private string? GetEquipmentValue(params string[] labels)
        {
            if (labels is null || labels.Length == 0)
                return null;

            var lines = SplitEquipmentLines(_equipmentText);

            foreach (var line in lines)
            {
                var parsed = ParseEquipmentEntry(line);
                if (!parsed.HasValue)
                    continue;

                foreach (var label in labels)
                {
                    if (string.Equals(parsed.Value.Label, label, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(parsed.Value.Value) ? null : parsed.Value.Value.Trim();
                }
            }

            return null;
        }

        private static List<string> SplitEquipmentLines(string? text)
        {
            return (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static (string Label, string Value)? ParseEquipmentEntry(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var idx = line.IndexOf(':');

            if (idx <= 0 || idx >= line.Length - 1)
                return null;

            var label = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                return null;

            return (label, value);
        }

        private static string MaskSensitiveValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string('•', Math.Max(8, value.Length));
        }

        public void SetCommunicationDeviceTypes(IEnumerable<CommunicationDeviceTypeDto>? deviceTypes)
        {
            _communicationDeviceTypes = (deviceTypes ?? Enumerable.Empty<CommunicationDeviceTypeDto>())
                .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.DisplayName))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        private object CreateSwapButtonContent()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            panel.Children.Add(new TextBlock
            {
                Text = "⇄",
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Swap",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private object CreateAddedReplacementButtonContent()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            panel.Children.Add(new TextBlock
            {
                Text = "\uE73E",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Added",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private object CreateTrashButtonContent()
        {
            return new Path
            {
                Width = 16,
                Height = 16,

                Data = Geometry.Parse(
                    "M4,6 L20,6 " +
                    "M9,6 L9,4 " +
                    "C9,2.9 9.9,2 11,2 " +
                    "L13,2 " +
                    "C14.1,2 15,2.9 15,4 " +
                    "L15,6 " +
                    "M6,6 L7,21 L17,21 L18,6 " +
                    "M10,10 L10,17 " +
                    "M14,10 L14,17"),

                Fill = Brushes.Transparent,
                Stroke = TryFindResource("TextSecondary") as Brush,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,

                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static string BuildReplacementEntryKey(string? label, string? oldSerial)
        {
            var cleanLabel = (label ?? string.Empty).Trim();
            var cleanOldSerial = (oldSerial ?? string.Empty).Trim();

            return $"{cleanLabel}|{cleanOldSerial}";
        }

        public bool TryValidateEquipmentReplacementEntriesForSubmit()
        {
            if (ReplacementEntriesPanel is null)
                return true;

            var rowNumber = 0;

            foreach (var rowBorder in
                     ReplacementEntriesPanel.Children.OfType<Border>())
            {
                rowNumber++;

                if (rowBorder.Tag is not ReplacementEntryRowTag rowTag)
                    continue;

                if (rowTag.UsesCommunicationDeviceTypePicker)
                {
                    var comboBox =
                        FindVisualChildByTag<ComboBox>(
                            rowBorder,
                            "ReplacementDeviceType");

                    if (comboBox is null)
                        continue;

                    var selectedDeviceType =
                        GetComboBoxSelectedText(comboBox);

                    if (!string.IsNullOrWhiteSpace(selectedDeviceType))
                    {
                        ClearRequiredFieldWarning(comboBox);
                        continue;
                    }

                    MarkRequiredFieldWarning(comboBox);

                    var rowLabel =
                        string.IsNullOrWhiteSpace(rowTag.Label)
                            ? $"replacement row {rowNumber}"
                            : $"{rowTag.Label} replacement";

                    MessageBox.Show(
                        $"Select a Device Type for the {rowLabel} before submitting the write-up.",
                        "Equipment Replacement Required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    comboBox.Focus();
                    return false;
                }

                /*
                 * Manually added replacement rows require an Item description.
                 * Old Serial and New Serial remain optional.
                 */
                var itemTextBox =
                    FindVisualChildByTag<TextBox>(
                        rowBorder,
                        "ReplacementItem");

                if (itemTextBox is null)
                    continue;

                if (!string.IsNullOrWhiteSpace(itemTextBox.Text))
                {
                    ClearRequiredFieldWarning(itemTextBox);
                    continue;
                }

                MarkRequiredFieldWarning(itemTextBox);

                MessageBox.Show(
                    $"Enter an Item for replacement row {rowNumber} before submitting the write-up.",
                    "Equipment Replacement Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                itemTextBox.Focus();
                return false;
            }

            return true;
        }

        private static string GetComboBoxSelectedText(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString()?.Trim() ?? string.Empty;

            return comboBox.SelectedItem?.ToString()?.Trim()
                   ?? comboBox.Text?.Trim()
                   ?? string.Empty;
        }

        private void MarkRequiredFieldWarning(Control control)
        {
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));
            control.BorderThickness = new Thickness(1.5);
        }

        private static void ClearRequiredFieldWarning(Control control)
        {
            control.ClearValue(Control.BorderBrushProperty);
            control.ClearValue(Control.BorderThicknessProperty);
        }

        private static T? FindVisualChildByTag<T>(DependencyObject root, object tag)
            where T : FrameworkElement
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);

            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is T typedChild &&
                    Equals(typedChild.Tag, tag))
                {
                    return typedChild;
                }

                var nested = FindVisualChildByTag<T>(child, tag);

                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private bool CanAddReplacementEntry(bool showMessage = true)
        {
            var currentCount = ReplacementEntriesPanel?.Children.Count ?? 0;

            if (currentCount < MaxReplacementEntries)
                return true;

            if (showMessage)
            {
                MessageBox.Show(
                    $"You can only add up to {MaxReplacementEntries} replacement entries at one time.",
                    "Replacement Entries Limit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }
    }
}