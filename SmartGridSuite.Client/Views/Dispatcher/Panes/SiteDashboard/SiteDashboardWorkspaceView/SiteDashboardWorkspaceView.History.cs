using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        public event EventHandler<SiteDashboardHistoryRowViewModel>? EditHistoryWriteUpRequested;

        public event EventHandler<SiteDashboardHistoryRowViewModel>? DeleteHistoryWriteUpRequested;

        public void SetHistoryRows(IEnumerable<SiteDashboardHistoryRowViewModel> rows)
        {
            HistoryDataGrid.ItemsSource = rows?.ToList() ?? new List<SiteDashboardHistoryRowViewModel>();
            HistoryDataGrid.SelectedItem = null;
            NarrativeTextBlock.Text = string.Empty;

            if (HistoryEditedTextBlock != null)
                HistoryEditedTextBlock.Text = string.Empty;
        }

        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is SiteDashboardHistoryRowViewModel row)
            {
                NarrativeTextBlock.Text = CleanNarrativeText(row.NarrativeText);

                if (HistoryEditedTextBlock != null)
                    HistoryEditedTextBlock.Text = row.EditedText;
            }
            else
            {
                NarrativeTextBlock.Text = string.Empty;

                if (HistoryEditedTextBlock != null)
                    HistoryEditedTextBlock.Text = string.Empty;
            }
        }

        private void EditHistoryWriteUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not SiteDashboardHistoryRowViewModel row ||
                !row.CanEditWriteUp)
            {
                return;
            }

            EditHistoryWriteUpRequested?.Invoke(this, row);
        }

        private void DeleteHistoryWriteUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not SiteDashboardHistoryRowViewModel row ||
                !row.CanEditWriteUp)
            {
                return;
            }

            DeleteHistoryWriteUpRequested?.Invoke(this, row);
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
    }
}