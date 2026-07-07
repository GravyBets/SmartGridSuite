using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private void ApplyTicketInfo(string rawText)
        {
            TicketNotificationNameTextBlock.Text = GetTicketFieldValue(rawText, "Notification Name");
            TicketNotificationNumberTextBlock.Text = GetTicketFieldValue(rawText, "Notification #");
            TicketProblemIssueTextBlock.Text = GetTicketFieldValue(rawText, "Problem/Issue");
            TicketDispatchNotesTextBlock.Text = GetTicketFieldValue(rawText, "Dispatch Notes");
            TicketWorkOrderTextBlock.Text = GetTicketFieldValue(rawText, "Work Order");
            TicketWorkOrderTypeTextBlock.Text = GetTicketFieldValue(rawText, "Work Order Type");
            TicketAssignedToTextBlock.Text = GetTicketFieldValue(rawText, "Assigned To");
            TicketDateCreatedTextBlock.Text = GetTicketFieldValue(rawText, "Date Created");
            TicketStatusTextBlock.Text = GetTicketFieldValue(rawText, "Current Status");

            if (string.IsNullOrWhiteSpace(TicketNotificationNameTextBlock.Text))
                TicketNotificationNameTextBlock.Text = "No ticket data returned yet.";

            if (string.IsNullOrWhiteSpace(TicketNotificationNumberTextBlock.Text))
                TicketNotificationNumberTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketProblemIssueTextBlock.Text))
                TicketProblemIssueTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketDispatchNotesTextBlock.Text))
                TicketDispatchNotesTextBlock.Text = "No dispatch notes.";

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

            ApplyTicketActionButtons();
            ApplyTicketStatusDisplay();
        }

        private static readonly string[] TicketInfoLabels =
        {
            "Notification Name",
            "Notification #",
            "Problem/Issue",
            "Dispatch Notes",
            "Work Order",
            "Work Order Type",
            "Assigned To",
            "Date Created",
            "Current Status"
        };

        private static string GetTicketFieldValue(string rawText, string label)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            var lines = rawText.Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.None);

            var prefix = label + ":";
            var capturing = false;
            var values = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();

                if (!capturing)
                {
                    if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var idx = trimmed.IndexOf(':');
                    if (idx >= 0)
                    {
                        var firstValue = trimmed[(idx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(firstValue))
                            values.Add(firstValue);
                    }

                    capturing = true;
                    continue;
                }

                if (IsTicketInfoLabelLine(trimmed))
                    break;

                if (!string.IsNullOrWhiteSpace(trimmed))
                    values.Add(trimmed);
            }

            return string.Join(Environment.NewLine, values).Trim();
        }

        private static bool IsTicketInfoLabelLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return TicketInfoLabels.Any(label =>
                line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshTicketButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshTicketRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void CopyTicketNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            await CopyButtonValueWithFeedbackAsync(
                sender,
                TicketNotificationNumberTextBlock.Text,
                "Copy notification");
        }

        private async void CopyTicketWorkOrderButton_Click(object sender, RoutedEventArgs e)
        {
            await CopyButtonValueWithFeedbackAsync(
                sender,
                TicketWorkOrderTextBlock.Text,
                "Copy work order");
        }

        private async Task CopyButtonValueWithFeedbackAsync(object sender, string? value, string defaultToolTip)
        {
            var cleanValue = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanValue) || cleanValue == "—")
                return;

            if (sender is not Button button)
                return;

            var copied = await TryCopyToClipboardAsync(cleanValue);

            if (!copied)
            {
                button.ToolTip = "Could not copy. Try again.";
                return;
            }

            if (button.Content is not TextBlock glyphBlock)
                return;

            var originalText = glyphBlock.Text;
            var originalToolTip = button.ToolTip;

            glyphBlock.Text = CheckGlyph;
            button.ToolTip = "Copied!";

            await Task.Delay(TimeSpan.FromSeconds(3));

            glyphBlock.Text = string.IsNullOrWhiteSpace(originalText)
                ? CopyGlyph
                : originalText;

            button.ToolTip = originalToolTip ?? defaultToolTip;
        }

        private void ApplyTicketActionButtons()
        {
            RequestTicketButton.Visibility = Visibility.Collapsed;
            RequestCapitalButton.Visibility = Visibility.Collapsed;
            RequestMaintenanceButton.Visibility = Visibility.Collapsed;

            RequestTicketButton.IsEnabled = false;
            RequestCapitalButton.IsEnabled = false;
            RequestMaintenanceButton.IsEnabled = false;

            var hasTicket = CurrentTicketId > 0 &&
                            !TicketNotificationNameTextBlock.Text.Equals(
                                "No ticket data returned yet.",
                                StringComparison.OrdinalIgnoreCase);

            var workOrderType = (TicketWorkOrderTypeTextBlock.Text ?? string.Empty).Trim();

            if (!hasTicket)
            {
                RequestTicketButton.Visibility = Visibility.Visible;
                RequestTicketButton.IsEnabled = true;

                TicketRequestsDescriptionTextBlock.Text =
                    "No ticket is associated with this site. You can create a ticket request.";

                return;
            }

            if (workOrderType.Equals("Maintenance", StringComparison.OrdinalIgnoreCase) ||
                workOrderType.Equals("Maint", StringComparison.OrdinalIgnoreCase))
            {
                RequestCapitalButton.Visibility = Visibility.Visible;
                RequestCapitalButton.IsEnabled = true;

                TicketRequestsDescriptionTextBlock.Text =
                    "This maintenance order can be requested as capital.";

                return;
            }

            if (workOrderType.Equals("Capital", StringComparison.OrdinalIgnoreCase) ||
                workOrderType.Equals("Cap", StringComparison.OrdinalIgnoreCase))
            {
                RequestMaintenanceButton.Visibility = Visibility.Visible;
                RequestMaintenanceButton.IsEnabled = true;

                TicketRequestsDescriptionTextBlock.Text =
                    "This capital order can be requested as maintenance.";

                return;
            }

            TicketRequestsDescriptionTextBlock.Text =
                "No request actions are available for the current ticket.";
        }

        private void ApplyTicketStatusDisplay()
        {
            TicketStatusBadge.ClearValue(Border.BackgroundProperty);
            TicketStatusBadge.ClearValue(Border.BorderBrushProperty);
            TicketStatusBadge.Background = Brushes.Transparent;
            TicketStatusBadge.BorderThickness = new Thickness(0);
        }

        private void RequestCapitalButton_Click(object sender, RoutedEventArgs e)
        {
            var reason = PromptForTicketActionReason("Request Capital");

            if (string.IsNullOrWhiteSpace(reason))
                return;

            TicketActionRequested?.Invoke(
                this,
                new TicketActionRequestedEventArgs(
                    action: "RequestCapital",
                    ticketId: CurrentTicketId,
                    reason: reason,
                    workOrderType: TicketWorkOrderTypeTextBlock.Text ?? string.Empty,
                    notification: TicketNotificationNumberTextBlock.Text ?? string.Empty,
                    workOrder: TicketWorkOrderTextBlock.Text ?? string.Empty));
        }

        private void RequestMaintenanceButton_Click(object sender, RoutedEventArgs e)
        {
            var reason = PromptForTicketActionReason("Request Maintenance");

            if (string.IsNullOrWhiteSpace(reason))
                return;

            TicketActionRequested?.Invoke(
                this,
                new TicketActionRequestedEventArgs(
                    action: "RequestMaintenance",
                    ticketId: CurrentTicketId,
                    reason: reason,
                    workOrderType: TicketWorkOrderTypeTextBlock.Text ?? string.Empty,
                    notification: TicketNotificationNumberTextBlock.Text ?? string.Empty,
                    workOrder: TicketWorkOrderTextBlock.Text ?? string.Empty));
        }

        private void RequestTicketButton_Click(object sender, RoutedEventArgs e)
        {
            var reason = PromptForTicketActionReason("Request Ticket");

            if (string.IsNullOrWhiteSpace(reason))
                return;

            TicketActionRequested?.Invoke(
                this,
                new TicketActionRequestedEventArgs(
                    action: "RequestTicket",
                    ticketId: CurrentTicketId,
                    reason: reason,
                    workOrderType: TicketWorkOrderTypeTextBlock.Text ?? string.Empty,
                    notification: TicketNotificationNumberTextBlock.Text ?? string.Empty,
                    workOrder: TicketWorkOrderTextBlock.Text ?? string.Empty));
        }

        private string? PromptForTicketActionReason(string actionTitle)
        {
            var dialog = new Window
            {
                Title = actionTitle,
                Width = 460,
                Height = 280,
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
                Text = actionTitle,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush
            });

            header.Children.Add(new TextBlock
            {
                Text = "Enter the reason for this request.",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            Grid.SetRow(header, 0);

            var reasonBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                MinHeight = 110
            };

            if (TryFindResource("ModernTextBox") is Style textBoxStyle)
                reasonBox.Style = textBoxStyle;

            Grid.SetRow(reasonBox, 2);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 92,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0)
            };

            if (TryFindResource("SecondaryButtonStyle") is Style secondaryStyle)
                cancelButton.Style = secondaryStyle;

            var submitButton = new Button
            {
                Content = "Continue",
                Width = 104,
                Height = 32,
                IsDefault = true
            };

            if (TryFindResource("PrimaryButtonStyle") is Style primaryStyle)
                submitButton.Style = primaryStyle;

            string? result = null;

            cancelButton.Click += (_, _) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            submitButton.Click += (_, _) =>
            {
                var reason = (reasonBox.Text ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(reason))
                {
                    MessageBox.Show(
                        dialog,
                        "Enter a reason before continuing.",
                        actionTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                result = reason;
                dialog.DialogResult = true;
                dialog.Close();
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(submitButton);

            Grid.SetRow(buttons, 4);

            root.Children.Add(header);
            root.Children.Add(reasonBox);
            root.Children.Add(buttons);

            dialog.Content = root;

            return dialog.ShowDialog() == true
                ? result
                : null;
        }

        public sealed class TicketActionRequestedEventArgs : EventArgs
        {
            public TicketActionRequestedEventArgs(
                string action,
                long ticketId,
                string reason,
                string workOrderType,
                string notification,
                string workOrder)
            {
                Action = action;
                TicketId = ticketId;
                Reason = reason;
                WorkOrderType = workOrderType;
                Notification = notification;
                WorkOrder = workOrder;
            }

            public string Action { get; }
            public long TicketId { get; }
            public string Reason { get; }
            public string WorkOrderType { get; }
            public string Notification { get; }
            public string WorkOrder { get; }
        }
    }
}