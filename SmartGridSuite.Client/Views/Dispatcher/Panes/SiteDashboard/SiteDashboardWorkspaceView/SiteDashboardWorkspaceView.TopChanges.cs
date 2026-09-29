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
    private TopChangeDto? _activeTopChangeRequest;

    private DateTime _topChangeIpCopyFeedbackUntilUtc =
        DateTime.MinValue;

    private string _topChangeCopiedIp =
        string.Empty;

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
        if (!supported)
        {
            _activeTopChangeRequest = null;
            return;
        }
        var ticketId = CurrentTicketId;
        if (ticketId <= 0)
        {
            _activeTopChangeRequest = null;
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
            _activeTopChangeRequest = request;

            ApplyTopChangeButtonState(
                request);
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
        /*
         * Once Dispatch has assigned the new IP, the field technician already
         * knows what the TOP change is for. The useful action here is getting
         * that IP into the clipboard so it can be pasted directly into the
         * dashboard IP field.
         */
        var activeRequest =
            _activeTopChangeRequest;

        if (activeRequest is not null &&
            activeRequest.State.Equals(
                "IpReady",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                activeRequest.NewIp))
        {
            var newIp =
                activeRequest.NewIp.Trim();

            try
            {
                Clipboard.SetText(
                    newIp);

                _topChangeCopiedIp =
                    newIp;

                _topChangeIpCopyFeedbackUntilUtc =
                    DateTime.UtcNow.AddSeconds(3);

                ApplyTopChangeButtonState(
                    activeRequest);

                await Task.Delay(
                    TimeSpan.FromSeconds(3));

                /*
                 * Only clear this feedback if it still belongs to the same IP.
                 * A newer TOP-change refresh/request must win.
                 */
                if (string.Equals(
                        _topChangeCopiedIp,
                        newIp,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _topChangeIpCopyFeedbackUntilUtc =
                        DateTime.MinValue;

                    _topChangeCopiedIp =
                        string.Empty;

                    ApplyTopChangeButtonState(
                        _activeTopChangeRequest);
                }
            }
            catch
            {
                TopChangeButton.Content =
                    "Copy failed — try again";

                TopChangeButton.ToolTip =
                    "Could not copy the assigned IP to the clipboard.";

                _topChangeIpCopyFeedbackUntilUtc =
                    DateTime.UtcNow.AddSeconds(3);

                _topChangeCopiedIp =
                    string.Empty;

                await Task.Delay(
                    TimeSpan.FromSeconds(3));

                _topChangeIpCopyFeedbackUntilUtc =
                    DateTime.MinValue;

                ApplyTopChangeButtonState(
                    _activeTopChangeRequest);
            }

            return;
        }

        if (_topChangeOpening)
            return;

        _topChangeOpening =
            true;

        try
        {
            await TopChangeWindow.OpenAsync(
                Window.GetWindow(this),
                CurrentTicketId,
                dispatch: false);
        }
        finally
        {
            _topChangeOpening =
                false;

            await RefreshTopChangeNoticeAsync();
        }
    }

    private void ApplyTopChangeButtonState(
        TopChangeDto? request)
    {
        if (TopChangeButton is null)
            return;

        if (request is null)
        {
            TopChangeButton.Content =
                "Request TOP Change";

            TopChangeButton.ToolTip =
                "Request a new TOP and sector.";

            return;
        }

        if (request.State.Equals(
                "IpReady",
                StringComparison.OrdinalIgnoreCase))
        {
            var newIp =
                (request.NewIp ?? string.Empty)
                    .Trim();

            var showingCopyFeedback =
                DateTime.UtcNow <
                    _topChangeIpCopyFeedbackUntilUtc &&
                !string.IsNullOrWhiteSpace(
                    _topChangeCopiedIp) &&
                string.Equals(
                    _topChangeCopiedIp,
                    newIp,
                    StringComparison.OrdinalIgnoreCase);

            TopChangeButton.Content =
                showingCopyFeedback
                    ? $"✓ Copied: {newIp}"
                    : $"New IP Ready: {newIp}";

            TopChangeButton.ToolTip =
                showingCopyFeedback
                    ? "Assigned IP copied to clipboard."
                    : "Click to copy the assigned IP.";

            return;
        }

        TopChangeButton.Content =
            "TOP Change — Waiting for IP";

        TopChangeButton.ToolTip =
            $"{request.NewTop} / {request.NewSector} — click for details";
    }
}
