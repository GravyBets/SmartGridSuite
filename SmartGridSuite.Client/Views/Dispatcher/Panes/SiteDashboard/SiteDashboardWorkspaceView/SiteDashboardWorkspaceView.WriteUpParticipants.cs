#nullable enable
using SmartGridSuite.Contracts.Tickets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private sealed class WriteUpParticipantPreviewResult
        {
            public string FinalWriteUpText { get; init; } = string.Empty;
            public string SiteHistoryWriteUpText { get; init; } = string.Empty;
            public List<SubmitTicketWriteUpTechnician> SelectedTechnicians { get; init; } = new();
        }

        private WriteUpParticipantPreviewResult? ShowWriteUpPreviewWindowWithParticipants(
            string finalWriteUpText,
            string siteHistoryWriteUpText)
        {
            var crewNames =
                ParseWriteUpCrewNames(
                    CurrentCnpTechName);

            var submittingTechnician =
                crewNames.FirstOrDefault() ?? string.Empty;

            var otherCrewMembers =
                crewNames
                    .Skip(1)
                    .ToList();

            var dialog = new Window
            {
                Title = "Submit Write-Up Preview",
                Width = 760,
                Height = otherCrewMembers.Count > 0 ? 680 : 580,
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

            Grid.SetRow(previewBox, 4);

            var crewCheckBoxes =
                new List<CheckBox>();

            if (otherCrewMembers.Count > 0)
            {
                var crewBorder = new Border
                {
                    Padding = new Thickness(12, 10, 12, 10),
                    BorderThickness = new Thickness(1),
                    BorderBrush = TryFindResource("CardBorder") as Brush,
                    Background = TryFindResource("CardBackground") as Brush,
                    CornerRadius = new CornerRadius(6)
                };

                var crewPanel = new StackPanel();

                crewPanel.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(submittingTechnician)
                        ? "Include crew members in this write-up:"
                        : $"Submitting technician: {submittingTechnician} (always included)",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TryFindResource("TextPrimary") as Brush
                });

                crewPanel.Children.Add(new TextBlock
                {
                    Text = "Uncheck another technician to exclude this write-up from their name and personal History.",
                    Margin = new Thickness(0, 3, 0, 7),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = TryFindResource("TextSecondary") as Brush
                });

                foreach (var technicianName in otherCrewMembers)
                {
                    var checkBox = new CheckBox
                    {
                        Content = technicianName,
                        Tag = technicianName,
                        IsChecked = true,
                        Margin = new Thickness(0, 2, 0, 2),
                        Foreground = TryFindResource("TextPrimary") as Brush
                    };

                    crewCheckBoxes.Add(checkBox);
                    crewPanel.Children.Add(checkBox);
                }

                crewBorder.Child = crewPanel;
                Grid.SetRow(crewBorder, 2);
                root.Children.Add(crewBorder);
            }

            List<string> GetSelectedNames()
            {
                if (crewNames.Count == 0)
                    return new List<string>();

                var selected = new List<string>
                {
                    crewNames[0]
                };

                selected.AddRange(
                    crewCheckBoxes
                        .Where(x => x.IsChecked == true)
                        .Select(x => x.Tag?.ToString() ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x)));

                return selected
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            void RefreshPreview()
            {
                var selectedNames = GetSelectedNames();

                previewBox.Text =
                    selectedNames.Count == 0
                        ? finalWriteUpText
                        : ReplaceWriteUpCrewFooter(
                            finalWriteUpText,
                            selectedNames);
            }

            foreach (var checkBox in crewCheckBoxes)
            {
                checkBox.Checked += (_, _) => RefreshPreview();
                checkBox.Unchecked += (_, _) => RefreshPreview();
            }

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

            Grid.SetRow(buttons, 6);

            root.Children.Add(header);
            root.Children.Add(previewBox);
            root.Children.Add(buttons);

            dialog.Content = root;

            RefreshPreview();

            if (dialog.ShowDialog() != true)
                return null;

            var selectedNames =
                GetSelectedNames();

            var selectedTechnicians =
                selectedNames
                    .Select(CreateWriteUpTechnicianSelection)
                    .ToList();

            return new WriteUpParticipantPreviewResult
            {
                FinalWriteUpText =
                    selectedNames.Count == 0
                        ? finalWriteUpText
                        : ReplaceWriteUpCrewFooter(
                            finalWriteUpText,
                            selectedNames),

                SiteHistoryWriteUpText =
                    selectedNames.Count == 0
                        ? siteHistoryWriteUpText
                        : ReplaceWriteUpCrewFooter(
                            siteHistoryWriteUpText,
                            selectedNames),

                SelectedTechnicians =
                    selectedTechnicians
            };
        }

        private static List<string> ParseWriteUpCrewNames(string? crewDisplayText)
        {
            var text =
                (crewDisplayText ?? string.Empty)
                .Trim();

            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return Regex
                .Split(text, @"\s*(?:,|&)\s*")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static SubmitTicketWriteUpTechnician CreateWriteUpTechnicianSelection(
            string technicianNameOrEmployeeId)
        {
            var value =
                (technicianNameOrEmployeeId ?? string.Empty)
                .Trim();

            var looksLikeEmployeeId =
                value.Length > 0 &&
                value.All(char.IsDigit);

            return new SubmitTicketWriteUpTechnician
            {
                EmployeeId = looksLikeEmployeeId
                    ? value
                    : string.Empty,

                TechnicianName = value
            };
        }

        private static string ReplaceWriteUpCrewFooter(
            string? writeUpText,
            IReadOnlyList<string> technicianNames)
        {
            var cleanWriteUp =
                (writeUpText ?? string.Empty)
                .Trim();

            var crewDisplay =
                FormatPreviewCrewDisplayText(
                    technicianNames);

            if (string.IsNullOrWhiteSpace(crewDisplay))
                return cleanWriteUp;

            cleanWriteUp = Regex.Replace(
                    cleanWriteUp,
                    @"(?:\r?\n){0,2}-{10,}\r?\nCNP Techs:\s*[^\r\n]*\s*$",
                    string.Empty,
                    RegexOptions.IgnoreCase)
                .TrimEnd();

            var footer =
                "----------------------------" +
                Environment.NewLine +
                $"CNP Techs: {crewDisplay}";

            return string.IsNullOrWhiteSpace(cleanWriteUp)
                ? footer
                : cleanWriteUp +
                  Environment.NewLine +
                  footer;
        }

        private static string FormatPreviewCrewDisplayText(
            IReadOnlyList<string> names)
        {
            var cleanNames =
                names
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (cleanNames.Count == 0)
                return string.Empty;

            if (cleanNames.Count == 1)
                return cleanNames[0];

            if (cleanNames.Count == 2)
                return $"{cleanNames[0]} & {cleanNames[1]}";

            return string.Join(", ", cleanNames.Take(cleanNames.Count - 1)) +
                   " & " +
                   cleanNames.Last();
        }
    }
}
