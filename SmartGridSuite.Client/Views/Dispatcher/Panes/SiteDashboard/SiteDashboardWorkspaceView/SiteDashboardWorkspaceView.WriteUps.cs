using DocumentFormat.OpenXml.Bibliography;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private const string EquipmentWriteUpHeader = "----Equipment Replacements----";
        private const string PingWriteUpHeader = "----------Ping Stats----------";
        private const string SnmpWriteUpHeader = "----------SNMP Polls----------";
        private const string TicketWriteUpHeader = "------------Ticket------------";

        private void SubmitWriteUpButton_Click(object sender, RoutedEventArgs e)
        {
            FlushWriteUpTextChangedDebounce();

            if (!TryValidateEquipmentReplacementEntriesForSubmit())
                return;

            if (!TryBuildSubmitWriteUpText(
                out var finalWriteUpText,
                out var siteHistoryWriteUpText))
            {
                return;
            }

            var confirmed = ShowWriteUpPreviewWindow(finalWriteUpText);

            if (!confirmed)
                return;

            WriteUpSubmitRequested?.Invoke(
                this,
                new WriteUpSubmitRequestedEventArgs(
                    finalWriteUpText,
                    siteHistoryWriteUpText,
                    true,
                    IncludePingStatsCheckBox.IsChecked == true,
                    IncludeSnmpStatsCheckBox.IsChecked == true));
        }

        private bool IsTowerDashboard => string.Equals(EquipmentDashboardKind, SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Tower,
            StringComparison.OrdinalIgnoreCase);

        private bool TryBuildSubmitWriteUpText(out string finalWriteUpText, out string siteHistoryWriteUpText)
        {
            finalWriteUpText = string.Empty;
            siteHistoryWriteUpText = string.Empty;

            var sections = new List<string>();

            var reasonText = BuildWriteUpReasonText();

            if (!string.IsNullOrWhiteSpace(reasonText))
                sections.Add($"Reason: {reasonText}");

            var manualWriteUp = (WriteUpTextBox.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(manualWriteUp))
                sections.Add(manualWriteUp);

            if (!TryGetEquipmentReplacementLines(out var equipmentLines))
                return false;

            if (equipmentLines.Count > 0)
            {
                sections.Add(BuildSimpleWriteUpSection(
                    EquipmentWriteUpHeader,
                    equipmentLines));
            }

            var pingSection = string.Empty;

            if (IncludePingStatsCheckBox.IsChecked == true)
            {
                pingSection = IsTowerDashboard
                    ? GetTowerPingStatsForWriteUp()
                    : (PingStatsProvider?.Invoke()?.Trim() ?? string.Empty);

                if (string.IsNullOrWhiteSpace(pingSection))
                {
                    MessageBox.Show(
                        "Ping stats were selected, but no ping results are available yet.",
                        "Submit Write-Up",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                if (!IsTowerDashboard)
                    pingSection = AppendAssociatedTopToPingStats(pingSection);

                pingSection = StripLeadingWriteUpHeader(pingSection, "Ping Stats:");

                if (!string.IsNullOrWhiteSpace(pingSection))
                {
                    sections.Add(BuildSimpleWriteUpSection(
                        PingWriteUpHeader,
                        pingSection));
                }
            }

            var snmpSection = string.Empty;

            if (IncludeSnmpStatsCheckBox.IsChecked == true)
            {
                snmpSection = BuildSnmpStatsWriteUpSection();

                if (string.IsNullOrWhiteSpace(snmpSection))
                {
                    MessageBox.Show(
                        "SNMP stats were selected, but no useful SNMP results are available yet. Poll SNMP values first or uncheck SNMP stats.",
                        "Submit Write-Up",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                snmpSection = StripLeadingWriteUpHeader(snmpSection, "SNMP Polls:");

                if (!string.IsNullOrWhiteSpace(snmpSection))
                {
                    sections.Add(BuildSimpleWriteUpSection(
                        SnmpWriteUpHeader,
                        snmpSection));
                }
            }

            var dispatcherSections = sections.ToList();
            var siteHistorySections = sections.ToList();

            var ticketSection = BuildTicketWriteUpFooterSection();

            if (!string.IsNullOrWhiteSpace(ticketSection))
            {
                siteHistorySections.Add(BuildSimpleWriteUpSection(
                    TicketWriteUpHeader,
                    ticketSection));
            }

            var cnpTechFooter = BuildCnpTechFooterSection();

            if (!string.IsNullOrWhiteSpace(cnpTechFooter))
            {
                dispatcherSections.Add(cnpTechFooter);
                siteHistorySections.Add(cnpTechFooter);
            }

            finalWriteUpText = string.Join(
                Environment.NewLine,
                dispatcherSections.Where(x => !string.IsNullOrWhiteSpace(x)));

            siteHistoryWriteUpText = string.Join(
                Environment.NewLine,
                siteHistorySections.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(finalWriteUpText))
            {
                MessageBox.Show(
                    "There is no write-up content to submit.",
                    "Submit Write-Up",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }

            return true;
        }

        private static string BuildSimpleWriteUpSection(string header, IEnumerable<string> lines)
        {
            var cleanLines = lines
                .Select(x => (x ?? string.Empty).TrimEnd())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (cleanLines.Count == 0)
                return string.Empty;

            return header + Environment.NewLine + string.Join(Environment.NewLine, cleanLines);
        }

        private static string StripLeadingWriteUpHeader(string text, string header)
        {
            text = (text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .ToList();

            if (lines.Count == 0)
                return text;

            if (string.Equals(lines[0].Trim(), header.Trim(), StringComparison.OrdinalIgnoreCase))
                lines.RemoveAt(0);

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);

            return string.Join(Environment.NewLine, lines).Trim();
        }

        private string BuildWriteUpReasonText()
        {
            // Prefer the real Problem/Issue field if it exists.
            var problem = GetNamedTextValue(
                "TicketProblemTextBlock",
                "TicketIssueTextBlock",
                "TicketProblemIssueTextBlock",
                "TicketProblemIssueValueTextBlock");

            if (!string.IsNullOrWhiteSpace(problem))
                return CleanTicketReferenceValue(problem);

            // Fallback: do NOT use the manual write-up as the reason anymore.
            return string.Empty;
        }

        private string BuildCnpTechFooterSection()
        {
            var techName = CleanTicketReferenceValue(CurrentCnpTechName);

            if (string.IsNullOrWhiteSpace(techName))
                return string.Empty;

            return "----------------------------" +
                   Environment.NewLine +
                   $"CNP Techs: {techName}";
        }

        private string BuildTicketWriteUpFooterSection()
        {
            var lines = new List<string>();

            var notificationName = CleanTicketReferenceValue(GetNamedTextValue(
                "TicketNotificationNameTextBlock",
                "NotificationNameTextBlock"));

            var notification = CleanTicketReferenceValue(GetNamedTextValue(
                "TicketNotificationNumberTextBlock",
                "NotificationNumberTextBlock"));

            var workOrder = CleanTicketReferenceValue(GetNamedTextValue(
                "TicketWorkOrderTextBlock",
                "WorkOrderTextBlock"));

            if (!string.IsNullOrWhiteSpace(notificationName))
                lines.Add(notificationName);

            if (!string.IsNullOrWhiteSpace(notification))
                lines.Add($"Notification: {notification}");

            if (!string.IsNullOrWhiteSpace(workOrder))
                lines.Add($"Work Order: {workOrder}");

            return lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines);
        }

        private string GetNamedTextValue(params string[] names)
        {
            foreach (var name in names)
            {
                if (FindName(name) is TextBlock textBlock)
                {
                    var value = CleanTicketReferenceValue(textBlock.Text);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                if (FindName(name) is TextBox textBox)
                {
                    var value = CleanTicketReferenceValue(textBox.Text);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return string.Empty;
        }

        private static string CleanTicketReferenceValue(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text) ||
                text == "—" ||
                text.Equals("No ticket data returned yet.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return text;
        }

        private bool TryGetEquipmentReplacementLines(out List<string> lines)
        {
            lines = new List<string>();

            if (ReplacementEntriesPanel is null)
                return true;

            foreach (var child in ReplacementEntriesPanel.Children)
            {
                if (child is not Border rowBorder)
                    continue;

                if (rowBorder.Tag is not ReplacementEntryRowTag rowTag)
                    continue;

                var entry = GetEquipmentReplacementEntry(rowBorder, rowTag);

                var isCompletelyBlank =
                    string.IsNullOrWhiteSpace(entry.Item) &&
                    string.IsNullOrWhiteSpace(entry.OldSerial) &&
                    string.IsNullOrWhiteSpace(entry.NewSerial);

                if (isCompletelyBlank)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.Item))
                {
                    MessageBox.Show(
                        "One replacement entry is missing an item/device type.",
                        "Equipment Replacement",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                lines.Add(BuildEquipmentReplacementLine(entry));
            }

            return true;
        }

        private static string BuildEquipmentReplacementLine(EquipmentReplacementWriteUpEntry entry)
        {
            var item =
                FriendlyReplacementItemLabel(entry.Item);

            var oldSerial =
                entry.OldSerial.Trim();

            var newSerial =
                entry.NewSerial.Trim();

            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(oldSerial))
            {
                lines.Add(
                    $"Found {item} SN: {oldSerial}");
            }

            if (!string.IsNullOrWhiteSpace(newSerial))
            {
                lines.Add(
                    $"Left {item} SN: {newSerial}");
            }

            /*
             * Item is required, but serial numbers are optional.
             * Preserve an item-only replacement instead of silently dropping it.
             */
            if (lines.Count == 0)
            {
                lines.Add(
                    $"Equipment replacement: {item}");
            }

            return string.Join(
                Environment.NewLine,
                lines);
        }

        private EquipmentReplacementWriteUpEntry GetEquipmentReplacementEntry(Border rowBorder, ReplacementEntryRowTag rowTag)
        {
            var item = rowTag.UsesCommunicationDeviceTypePicker
                ? GetTaggedComboBoxValue(rowBorder, "ReplacementDeviceType")
                : GetTaggedTextBoxValue(rowBorder, "ReplacementItem");

            return new EquipmentReplacementWriteUpEntry
            {
                SlotLabel = rowTag.Label,
                UsesCommunicationDeviceTypePicker = rowTag.UsesCommunicationDeviceTypePicker,
                Item = FriendlyReplacementItemLabel(item),
                OldSerial = GetTaggedTextBoxValue(rowBorder, "ReplacementOldSerial"),
                NewSerial = GetTaggedTextBoxValue(rowBorder, "ReplacementNewSerial")
            };
        }

        private static string FriendlyReplacementItemLabel(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.EndsWith(" SN", StringComparison.OrdinalIgnoreCase))
                text = text[..^3].Trim();

            if (string.Equals(text, "Primary Communications", StringComparison.OrdinalIgnoreCase))
                return "Primary Communications";

            if (string.Equals(text, "Secondary Communications", StringComparison.OrdinalIgnoreCase))
                return "Secondary Communications";

            return text;
        }

        private void IncludeSnmpStatsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var includeSnmp = IncludeSnmpStatsCheckBox.IsChecked == true;

            SnmpCategoryOptionsPanel.Visibility = includeSnmp
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (includeSnmp && !_snmpCategoryOptionsInitialized)
            {
                IncludeSnmpAdminCheckBox.IsChecked = true;
                IncludeSnmpConfigCheckBox.IsChecked = true;
                IncludeSnmpStatsCategoryCheckBox.IsChecked = true;

                _snmpCategoryOptionsInitialized = true;
            }
        }

        private HashSet<string> GetSelectedSnmpWriteUpCategories()
        {
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (IncludeSnmpAdminCheckBox.IsChecked == true)
                categories.Add("Admin");

            if (IncludeSnmpConfigCheckBox.IsChecked == true)
                categories.Add("Config");

            if (IncludeSnmpStatsCategoryCheckBox.IsChecked == true)
                categories.Add("Stats");

            return categories;
        }

        private string BuildSnmpStatsWriteUpSection()
        {
            if (SnmpCategoryItemsControl?.ItemsSource is not IEnumerable categories)
                return string.Empty;

            var selectedCategories = GetSelectedSnmpWriteUpCategories();

            if (selectedCategories.Count == 0)
                return string.Empty;

            var categoryOrder = new List<string>();
            var groupedLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var categoryObject in categories.Cast<object>())
            {
                var categoryName = GetObjectTextProperty(categoryObject, "Category");

                if (string.IsNullOrWhiteSpace(categoryName))
                    categoryName = "SNMP";

                categoryName = categoryName.Trim();

                if (!selectedCategories.Contains(categoryName))
                    continue;

                var rows = GetObjectEnumerableProperty(categoryObject, "Rows");

                if (rows is null)
                    continue;

                foreach (var row in rows)
                {
                    var label = GetObjectTextProperty(row, "Label");
                    var rawResult = GetObjectTextProperty(row, "ResultText");

                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    if (!IsUsefulSnmpResultText(rawResult))
                        continue;

                    var result = NormalizeSnmpResultForWriteUp(rawResult);

                    result = RemoveSpaceBeforeSnmpRatioSuffix(result);

                    if (string.IsNullOrWhiteSpace(result))
                        continue;

                    var line = $"{label.Trim()}: {result}";

                    var seenKey = $"{categoryName}|{line}";
                    if (!seenLines.Add(seenKey))
                        continue;

                    if (!groupedLines.ContainsKey(categoryName))
                    {
                        groupedLines[categoryName] = new List<string>();
                        categoryOrder.Add(categoryName);
                    }

                    groupedLines[categoryName].Add(line);
                }
            }

            if (groupedLines.Count == 0)
                return string.Empty;

            var output = new List<string>
            {
                "SNMP Polls:"
            };

            foreach (var categoryName in categoryOrder)
            {
                if (!groupedLines.TryGetValue(categoryName, out var lines) || lines.Count == 0)
                    continue;

                output.Add($"{categoryName}-");
                output.AddRange(lines);
            }

            return string.Join(Environment.NewLine, output);
        }

        private static string GetObjectTextProperty(object source, string propertyName)
        {
            var prop = source.GetType().GetProperty(propertyName);

            var value = prop?.GetValue(source);

            return value?.ToString()?.Trim() ?? string.Empty;
        }

        private static IEnumerable<object>? GetObjectEnumerableProperty(object source, string propertyName)
        {
            var prop = source.GetType().GetProperty(propertyName);

            if (prop?.GetValue(source) is IEnumerable enumerable)
                return enumerable.Cast<object>();

            return null;
        }

        private string AppendAssociatedTopToPingStats(string pingStats)
        {
            var top = (TopAccessTitleTextBlock.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(top) ||
                top.Equals("TOP Access", StringComparison.OrdinalIgnoreCase))
            {
                return pingStats;
            }

            return pingStats.TrimEnd() +
               Environment.NewLine +
               $"Database says Associated TOP: {top}. Please update if incorrect.";
        }

        private static string RemoveSpaceBeforeSnmpRatioSuffix(string? value)
        {
            var cleanValue =
                (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanValue))
                return string.Empty;

            /*
             * Removes whitespace only when it occurs between a numeric
             * value and a ratio suffix.
             *
             * 1.2 :1     -> 1.2:1
             * -70.0 dBm  -> -70.0 dBm
             * 38.4 dB    -> 38.4 dB
             */
            return System.Text.RegularExpressions.Regex.Replace(
                cleanValue,
                @"(?<=\d)\s+(?=:\s*\d)",
                string.Empty);
        }

        private bool ShowWriteUpPreviewWindow(string finalWriteUpText)
        {
            var dialog = new Window
            {
                Title = "Submit Write-Up Preview",
                Width = 760,
                Height = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this),
                Background = TryFindResource("AppBackground") as Brush
            };

            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel();

            header.Children.Add(new TextBlock
            {
                Text = "Review Write-Up Before Submit",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush
            });

            header.Children.Add(new TextBlock
            {
                Text = "Confirm this is exactly what should be submitted to the ticket.",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            Grid.SetRow(header, 0);

            var previewBox = new TextBox
            {
                Text = finalWriteUpText,
                AcceptsReturn = true,
                Height = double.NaN,
                VerticalAlignment = VerticalAlignment.Stretch,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsReadOnly = true,
                Padding = new Thickness(10),
                FontSize = 13
            };

            if (TryFindResource("ModernTextBox") is Style textBoxStyle)
                previewBox.Style = textBoxStyle;

            Grid.SetRow(previewBox, 2);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 94,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            if (TryFindResource("SecondaryButtonStyle") is Style secondaryStyle)
                cancelButton.Style = secondaryStyle;

            var confirmButton = new Button
            {
                Content = "Confirm",
                Width = 104,
                Height = 32,
                IsDefault = true
            };

            if (TryFindResource("PrimaryButtonStyle") is Style primaryStyle)
                confirmButton.Style = primaryStyle;

            cancelButton.Click += (_, _) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            confirmButton.Click += (_, _) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(confirmButton);

            Grid.SetRow(buttons, 4);

            root.Children.Add(header);
            root.Children.Add(previewBox);
            root.Children.Add(buttons);

            dialog.Content = root;

            return dialog.ShowDialog() == true;
        }

        public sealed class WriteUpSubmitRequestedEventArgs : EventArgs
        {
            public WriteUpSubmitRequestedEventArgs(
                string finalWriteUpText,
                string siteHistoryWriteUpText,
                bool includeEquipmentReplacements,
                bool includePingStats,
                bool includeSnmpStats)
            {
                FinalWriteUpText = finalWriteUpText;
                SiteHistoryWriteUpText = siteHistoryWriteUpText;
                IncludeEquipmentReplacements = includeEquipmentReplacements;
                IncludePingStats = includePingStats;
                IncludeSnmpStats = includeSnmpStats;
            }

            public string FinalWriteUpText { get; }
            public string SiteHistoryWriteUpText { get; }

            public bool IncludeEquipmentReplacements { get; }
            public bool IncludePingStats { get; }
            public bool IncludeSnmpStats { get; }
        }

        private static string BuildSimpleWriteUpSection(string header, string body)
        {
            body = (body ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            return header + Environment.NewLine + body;
        }
    }
}