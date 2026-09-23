using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Dialogs;
using SmartGridSuite.Contracts.Tickets;
using System.Windows;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;

public partial class SiteDashboardWorkspaceView
{
    private DispatcherTimer? _topChangeTimer;
    private readonly ApiClient _topChangeApi = ClientAppSettings.CreateApiClient();
    private bool _topChangeNoticeLoading;
    private bool _topChangeOpening;

    private void InitializeTopChangeRefresh()
    {
        _topChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _topChangeTimer.Tick += async (_, _) => await RefreshTopChangeNoticeAsync();
        Loaded += async (_, _) => { _topChangeTimer.Start(); await RefreshTopChangeNoticeAsync(); };
        Unloaded += (_, _) => _topChangeTimer.Stop();
    }

    private async Task RefreshTopChangeNoticeAsync()
    {
        if (TopChangeButton == null) return;
        var supported = EquipmentDashboardKind == "AMS" || EquipmentDashboardKind == "DACs" || EquipmentDashboardKind == "IGSD";
        TopChangeButton.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        if (!supported) return;
        var ticketId = CurrentTicketId;
        if (ticketId <= 0)
        {
            TopChangeButton.Content = "Request TOP Change";
            TopChangeButton.ToolTip = "Open an existing ticket for this site first.";
            return;
        }
        if (_topChangeNoticeLoading || !IsLoaded) return;
        _topChangeNoticeLoading = true;
        try
        {
            var request = await _topChangeApi
                .GetAsync<TopChangeDto>($"api/tickets/{ticketId}/top-change/notice");
            if (CurrentTicketId != ticketId) return;
            TopChangeButton.Content = request == null ? "Request TOP Change"
                : request.State == "IpReady" ? $"New IP Ready: {request.NewIp}" : "TOP Change — Waiting for IP";
            TopChangeButton.ToolTip = request == null ? "Request a new TOP and sector."
                : $"{request.NewTop} / {request.NewSector} — click for details";
        }
        catch
        {
            if (CurrentTicketId == ticketId)
            {
                TopChangeButton.Content = "TOP Change — Check Status";
                TopChangeButton.ToolTip = "Unable to refresh TOP-change status. Click to retry.";
            }
        }
        finally
        {
            _topChangeNoticeLoading = false;
            if (CurrentTicketId != ticketId && IsLoaded) _ = RefreshTopChangeNoticeAsync();
        }
    }

    private async void TopChangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topChangeOpening) return;
        _topChangeOpening = true;
        try { await TopChangeWindow.OpenAsync(Window.GetWindow(this), CurrentTicketId, dispatch: false); }
        finally { _topChangeOpening = false; await RefreshTopChangeNoticeAsync(); }
    }
}
