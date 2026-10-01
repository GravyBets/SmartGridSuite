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
            var query =
                (SearchTextBox.Text ?? string.Empty)
                    .Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                StatusTextBlock.Text =
                    "Enter a site, IP, SIM, serial number, notification, Work Order, or other identifier.";

                return;
            }

            _searchCts?.Cancel();
            _searchCts?.Dispose();

            _searchCts =
                new CancellationTokenSource();

            var ct =
                _searchCts.Token;

            SearchButton.IsEnabled = false;
            SearchTextBox.IsEnabled = false;

            StatusTextBlock.Text =
                $"Searching for {query}...";

            try
            {
                var result =
                    await _api.GetAsync<DeviceLookupResponseDto>(
                        $"api/device-lookup?query={Uri.EscapeDataString(query)}",
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

                var total =
                    result.ParentRecords.Count +
                    result.Tickets.Count +
                    result.SiteHistory.Count;

                var summary =
                    total == 0
                        ? $"No records found for {query}."
                        : $"Found {result.ParentRecords.Count} Parent DB/device record(s), " +
                          $"{result.RelatedSiteIds.Count} related site(s), " +
                          $"{result.Tickets.Count} ticket(s), and " +
                          $"{result.SiteHistory.Count} Site History record(s).";

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
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text =
                    $"Device Lookup failed: {ex.Message}";
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    SearchButton.IsEnabled = true;
                    SearchTextBox.IsEnabled = true;
                    SearchTextBox.Focus();
                }
            }
        }
    }
}
