using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        public void SetHistoryRows(IEnumerable<SiteDashboardHistoryRowViewModel> rows)
        {
            HistoryDataGrid.ItemsSource = rows?.ToList() ?? new List<SiteDashboardHistoryRowViewModel>();
            HistoryDataGrid.SelectedItem = null;
            NarrativeTextBlock.Text = string.Empty;
        }

        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is SiteDashboardHistoryRowViewModel row)
                NarrativeTextBlock.Text = CleanNarrativeText(row.NarrativeText);
            else
                NarrativeTextBlock.Text = string.Empty;
        }

        //Removes \n\n in narrative texts
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