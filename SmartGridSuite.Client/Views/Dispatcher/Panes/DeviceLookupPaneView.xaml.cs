using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Dispatcher;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class DeviceLookupPaneView : UserControl
    {
        private readonly ApiClient _api =
            ClientAppSettings.CreateApiClient();

        private CancellationTokenSource? _searchCts;

        public DeviceLookupPaneView()
        {
            InitializeComponent();
        }

        private void DeviceLookupPaneView_Unloaded(object sender, RoutedEventArgs e)
        {
            _searchCts?.Cancel();
        }

        private async void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await SearchAsync();
        }

        private async void SearchTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await SearchAsync();
        }

        private async Task SearchAsync()
        {
            if (_searchCts != null)
                return;

            var query =
                (SearchTextBox.Text ?? string.Empty)
                    .Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                StatusTextBlock.Text =
                    "Select a search type and enter its identifier.";

                return;
            }

            if (!Enum.TryParse<DeviceLookupSearchType>(
                    (SearchTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var searchType))
                return;

            ParentRecordsItemsControl.ItemsSource = null;
            RelatedSitesItemsControl.ItemsSource = null;
            TicketsDataGrid.ItemsSource = null;
            HistoryDataGrid.ItemsSource = null;
            SiteNotesDataGrid.ItemsSource = null;

            _searchCts =
                new CancellationTokenSource();

            var ct =
                _searchCts.Token;

            SearchTypeComboBox.IsEnabled = false;
            SearchButton.IsEnabled = false;
            SearchTextBox.IsEnabled = false;

            StatusTextBlock.Text =
                $"Searching for {query}...";

            try
            {
                var result =
                    await _api.GetAsync<DeviceLookupResponseDto>(
                        $"api/device-lookup?query={Uri.EscapeDataString(query)}&searchType={searchType}",
                        ct);

                if (ct.IsCancellationRequested)
                    return;

                result ??=
                    new DeviceLookupResponseDto
                    {
                        Query = query
                    };

                ParentRecordsItemsControl.ItemsSource =
                    result.ParentRecords;

                RelatedSitesItemsControl.ItemsSource =
                    result.RelatedSiteIds;

                TicketsDataGrid.ItemsSource =
                    result.Tickets;

                HistoryDataGrid.ItemsSource =
                    result.SiteHistory;

                SiteNotesDataGrid.ItemsSource =
                    result.SiteNotes;

                var total =
                    result.ParentRecords.Count +
                    result.Tickets.Count +
                    result.SiteHistory.Count +
                    result.SiteNotes.Count;

                var summary =
                    total == 0
                        ? $"No records returned for {query}."
                        : $"Found {result.ParentRecords.Count} Parent DB/device record(s), " +
                          $"{result.RelatedSiteIds.Count} related site(s), " +
                          $"{result.Tickets.Count} ticket(s), " +
                          $"{result.SiteHistory.Count} Site History record(s), and " +
                          $"{result.SiteNotes.Count} Site Note(s).";

                if (!string.IsNullOrWhiteSpace(
                        result.Warning))
                {
                    summary +=
                        $" {result.Warning}";
                }

                StatusTextBlock.Text =
                    summary;
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Search canceled.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text =
                    $"Device Lookup failed: {ex.Message}";
            }
            finally
            {
                _searchCts?.Dispose();
                _searchCts = null;
                SearchTypeComboBox.IsEnabled = true;
                SearchButton.IsEnabled = true;
                SearchTextBox.IsEnabled = true;
                if (IsLoaded)
                    SearchTextBox.Focus();
            }
        }
    }
}
