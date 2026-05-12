using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private const string CopyGlyph = "\uE8C8";
        private const string CheckGlyph = "\uE73E";

        private static string GetTaggedTextBoxValue(DependencyObject root, string tag)
        {
            return FindVisualChildren<TextBox>(root)
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                ?.Text
                ?.Trim()
                ?? string.Empty;
        }

        private static string GetTaggedComboBoxValue(DependencyObject root, string tag)
        {
            var comboBox = FindVisualChildren<ComboBox>(root)
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

            if (comboBox?.SelectedItem is null)
                return string.Empty;

            return comboBox.SelectedItem.ToString()?.Trim() ?? string.Empty;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent)
            where T : DependencyObject
        {
            if (parent is null)
                yield break;

            var childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                    yield return typedChild;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private async Task<bool> TryCopyToClipboardAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Clipboard can be temporarily locked by Windows, Teams, Excel, remote sessions, etc.
            // Retry a few times instead of crashing the app.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (COMException)
                {
                    await Task.Delay(60);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private TextBlock CreateTinyInlineCopyIcon(string tooltip)
        {
            return new TextBlock
            {
                Text = CopyGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Foreground = TryFindResource("TextSecondary") as Brush,
                Width = 16,
                Height = 16,
                Margin = new Thickness(6, 0, -8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private FrameworkElement CreateValueStack(string label, string value)
        {
            return CreateValueStack(label, value, new Thickness(0));
        }

        private FrameworkElement CreateValueStack(string label, string value, Thickness margin)
        {
            var stack = new StackPanel
            {
                Margin = margin
            };

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = TryFindResource("TextPrimary") as Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

            return stack;
        }
    }
}