using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public partial class TopChangeWindow : Window
{
    private readonly ApiClient _api = ClientAppSettings.CreateApiClient();
    private readonly TopChangeContextDto _context;
    private readonly Guid _requestId = Guid.NewGuid();
    private bool _saving;
    private int _copyFeedbackVersion;
    private static string Actor => (WindowsIdentity.GetCurrent()?.Name ?? "").Split('\\').Last().Split('@').First().Trim();

    public static async Task OpenAsync(Window? owner, long ticketId, bool dispatch)
    {
        if (ticketId <= 0)
        {
            MessageBox.Show("Open an existing ticket for this site before requesting a TOP change.", "TOP Change");
            return;
        }
        try
        {
            var context = await ClientAppSettings.CreateApiClient()
                .GetAsync<TopChangeContextDto>($"api/tickets/{ticketId}/top-change");
            if (context == null) return;
            new TopChangeWindow(context, dispatch) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    public TopChangeWindow() : this(new TopChangeContextDto(), false) { }

    private TopChangeWindow(TopChangeContextDto context, bool dispatch)
    {
        _context = context;
        InitializeComponent();
        Tag = dispatch ? "Dispatch" : "Technician";
        DataContext = context;
        Title = $"TOP Change — {context.Site}";
        SiteHeading.Text = $"Site: {context.Site}";
        CurrentTopCombo.ItemsSource = context.Sectors.Select(x => x.Top).Append(context.CurrentTop)
            .Distinct().OrderBy(x => x).ToList();
        CurrentTopCombo.SelectedItem = context.CurrentTop;
        CurrentSectorCombo.ItemsSource = context.Sectors.Where(x => x.Top == context.CurrentTop)
            .Select(x => x.Sector).Append(context.CurrentSector).Distinct().OrderBy(x => x).ToList();
        CurrentSectorCombo.SelectedItem = context.CurrentSector;
        CurrentIpTextBox.Text = context.CurrentIp;
        CurrentSiteCard.Visibility = dispatch ? Visibility.Collapsed : Visibility.Visible;
        ExistingTopPanel.Visibility = dispatch ? Visibility.Visible : Visibility.Collapsed;
        CurrentSitePanel.IsEnabled = !dispatch && (context.Request == null || context.Request.State == "Completed");
        if (context.Request is { State: not "Completed" } row)
        {
            ExistingRequestPanel.Visibility = Visibility.Visible;
            CancelButton.Content = "Close";
            ShowRequest(row);
            DispatchPanel.Visibility = dispatch ? Visibility.Visible : Visibility.Collapsed;
            SaveAssignedIpButton.Visibility = dispatch ? Visibility.Visible : Visibility.Collapsed;
            AssignedIpTextBox.IsReadOnly = !dispatch || row.State == "IpReady";
        }
        else if (dispatch)
            NoRequestText.Visibility = Visibility.Visible;
        else
        {
            NewRequestPanel.Visibility = Visibility.Visible;
            SubmitButton.Visibility = Visibility.Visible;
            TopCombo.ItemsSource = context.Sectors
                .GroupBy(x => x.Top, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First().TopDisplay)
                .OrderBy(x => x)
                .ToList();
            if (context.Sectors.Count == 0)
                MessageText.Text = "No TOP sectors are available. Refresh the server's tower cache before requesting a change.";
        }
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
        Closed += (_, _) => _copyFeedbackVersion++;
    }

    private void ShowRequest(TopChangeDto row)
    {
        RequestStateText.Text = row.State == "IpReady" ? "New IP Ready" : "Waiting for IP";
        RequestedTopText.Text = TopChangeRequestText.TopSector(row.NewTop, row.NewSector);
        RequestedBaseIpText.Text = $"Base IP: {(string.IsNullOrWhiteSpace(_context.RequestedBaseIp) ? "unavailable" : _context.RequestedBaseIp)}";
        RequestedBaseIpSourceText.Text = _context.RequestedBaseIpSource;
        CurrentReferenceText.Text = $"{TopChangeRequestText.TopSector(row.OldTop, row.OldSector)}\nCurrent IP: {row.OldIp}";
        RequestDetailsText.Text = $"Requested: {row.RequestedAt:g}";
        AssignedIpTextBox.Text = row.NewIp;
        CopyIpButton.Visibility = row.State == "IpReady" ? Visibility.Visible : Visibility.Collapsed;
        RequestSummaryTextBox.Text = TopChangeRequestText.Format(row, _context.RequestedBaseIp);
    }

    private void TopCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectorCombo == null) return;

        var selectedTop = GetSelectedRequestedTop();

        SectorCombo.ItemsSource = _context.Sectors
            .Where(x => 
                x.Top.Equals(
                    selectedTop, 
                    StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Sector)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        SectorCombo.SelectedIndex = -1;
    }

    private string GetSelectedRequestedTop()
    {
        var selectedText =
            (TopCombo.SelectedItem as string ?? TopCombo.Text ?? string.Empty).Trim();

        return _context.Sectors
            .FirstOrDefault(x =>
                x.Top.Equals(
                    selectedText, 
                    StringComparison.OrdinalIgnoreCase) ||
                x.TopDisplay.Equals(
                    selectedText, 
                    StringComparison.OrdinalIgnoreCase))
            ?.Top ?? string.Empty;
    }

    private void CurrentTopCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentSectorCombo == null) return;
        CurrentSectorCombo.ItemsSource = _context.Sectors
            .Where(x => x.Top == (CurrentTopCombo.SelectedItem as string))
            .Select(x => x.Sector)
            .Concat((CurrentTopCombo.SelectedItem as string) == _context.CurrentTop
                ? new[] { _context.CurrentSector } : Array.Empty<string>())
            .Distinct().OrderBy(x => x).ToList();
        CurrentSectorCombo.SelectedIndex = -1;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        var requestedTop = GetSelectedRequestedTop();
        var selected = _context.Sectors.FirstOrDefault(x =>
            x.Top.Equals(requestedTop, StringComparison.OrdinalIgnoreCase) &&
            x.Sector == (SectorCombo.SelectedItem as string));
        var currentTop = (CurrentTopCombo.SelectedItem as string ?? "").Trim();
        var currentSector = (CurrentSectorCombo.SelectedItem as string ?? "").Trim();
        var currentIp = CurrentIpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentTop) || string.IsNullOrWhiteSpace(currentSector))
        { MessageText.Text = "Select the current TOP and sector."; return; }
        if (selected == null)
        { MessageText.Text = "Select a TOP and sector."; return; }
        if (MessageBox.Show(this,
            $"Request {currentTop} / {currentSector} → {selected.Top} / {selected.Sector}?\n\nCurrent IP: {currentIp}\n\nDispatch will receive this ticket in TOP Change status.",
            "Confirm TOP Change", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            var saved = await _api.PostAsync<CreateTopChangeRequest, TopChangeDto>($"api/tickets/{_context.TicketId}/top-change",
                new CreateTopChangeRequest
                {
                    ClientRequestId = _requestId, NewSectorId = selected.SectorId,
                    AlreadyChanged = false, RequestedBy = Actor,
                    CurrentTop = currentTop, CurrentSector = currentSector, CurrentIp = currentIp,
                    ExpectedTop = _context.CurrentTop, ExpectedSector = _context.CurrentSector, ExpectedIp = _context.CurrentIp
                });
            if (saved == null) throw new InvalidOperationException("No response received. Reopen the request to check whether it was saved.");
        }, closeOnSuccess: true);
    }

    private async void SaveIp_Click(object sender, RoutedEventArgs e)
    {
        if (_context.Request is not { } row) return;
        await RunAsync(async () =>
        {
            var saved = await _api.PostAsync<AssignTopChangeIpRequest, TopChangeDto>(
                $"api/tickets/{row.TicketId}/top-change/{row.Id}/assign-ip",
                new AssignTopChangeIpRequest { Ip = AssignedIpTextBox.Text.Trim(), AssignedBy = Actor });
            if (saved == null) throw new InvalidOperationException("No response received. Refresh before retrying.");
            _context.Request = saved;
            ShowRequest(saved);
            AssignedIpTextBox.IsReadOnly = true;
            MessageText.Text = "IP saved and available to the technician in the app.";
        });
    }

    private async void CopyRequest_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(RequestSummaryTextBox.Text); }
        catch (Exception ex) { ShowError(ex); return; }

        var version = ++_copyFeedbackVersion;
        RequestCopyGlyph.Text = "\uE73E";
        CopyRequestButton.ToolTip = "Copied!";
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (version != _copyFeedbackVersion) return;
        RequestCopyGlyph.ClearValue(TextBlock.TextProperty);
        CopyRequestButton.ToolTip = "Copy IP request text";
    }
    private void CopyIp_Click(object sender, RoutedEventArgs e) => CopyText(_context.Request?.NewIp ?? "");
    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); MessageText.Text = "Copied."; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task RunAsync(Func<Task> action, bool closeOnSuccess = false)
    {
        if (_saving) return;
        _saving = true; Body.IsEnabled = false; MessageText.Text = "Saving...";
        var success = false;
        try { await action(); success = true; }
        catch (Exception ex) { MessageText.Text = "Request failed. Your entries remain available to retry."; ShowError(ex); }
        finally { _saving = false; Body.IsEnabled = true; }
        if (success && closeOnSuccess) Close();
    }

    private static void ShowError(Exception ex) => MessageBox.Show(
        ex is ApiClient.ApiException api ? api.Body ?? api.Message : ex.Message,
        "TOP Change", MessageBoxButton.OK, MessageBoxImage.Warning);

}
