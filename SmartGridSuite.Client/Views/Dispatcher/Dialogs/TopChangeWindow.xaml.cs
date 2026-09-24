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
        DataContext = context;
        Title = $"TOP Change — {context.Site}";
        SiteHeading.Text = $"Site: {context.Site}   •   Ticket: {context.TicketId}";
        CurrentTopCombo.ItemsSource = context.Sectors.Select(x => x.Top).Append(context.CurrentTop)
            .Distinct().OrderBy(x => x).ToList();
        CurrentTopCombo.SelectedItem = context.CurrentTop;
        CurrentSectorCombo.ItemsSource = context.Sectors.Where(x => x.Top == context.CurrentTop)
            .Select(x => x.Sector).Append(context.CurrentSector).Distinct().OrderBy(x => x).ToList();
        CurrentSectorCombo.SelectedItem = context.CurrentSector;
        CurrentIpTextBox.Text = context.CurrentIp;
        CurrentSitePanel.IsEnabled = !dispatch && (context.Request == null || context.Request.State == "Completed");
        if (context.Request is { State: not "Completed" } row)
        {
            ExistingRequestPanel.Visibility = Visibility.Visible;
            CancelButton.Content = "Close";
            ShowRequest(row);
            DispatchPanel.Visibility = dispatch ? Visibility.Visible : Visibility.Collapsed;
            AssignedIpTextBox.IsReadOnly = !dispatch || row.State == "IpReady";
        }
        else if (dispatch)
            NoRequestText.Visibility = Visibility.Visible;
        else
        {
            NewRequestPanel.Visibility = Visibility.Visible;
            SubmitButton.Visibility = Visibility.Visible;
            TopCombo.ItemsSource = context.Sectors.Select(x => x.Top).Distinct().OrderBy(x => x).ToList();
            if (context.Sectors.Count == 0)
                MessageText.Text = "No TOP sectors are available. Refresh the server's tower cache before requesting a change.";
        }
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }

    private void ShowRequest(TopChangeDto row)
    {
        RequestStateText.Text = row.State == "IpReady" ? "New IP Ready" : "Waiting for IP";
        RequestDetailsText.Text = $"New TOP: {row.NewTop}\nNew sector: {row.NewSector}\nRequested: {row.RequestedAt:g}\n" +
            (row.AlreadyChanged ? "TOP/sector was already changed when requested." : "TOP/sector change was planned when requested.");
        AssignedIpTextBox.Text = row.NewIp;
        CopyIpButton.Visibility = row.State == "IpReady" ? Visibility.Visible : Visibility.Collapsed;
        RequestSummaryTextBox.Text = $"Site: {row.Site}\nTicket: {row.TicketId}\n" +
            $"Current TOP: {row.OldTop}\nCurrent sector: {row.OldSector}\nCurrent IP: {row.OldIp}\n" +
            $"Requested TOP: {row.NewTop}\nRequested sector: {row.NewSector}\n" +
            $"Field work: {(row.AlreadyChanged ? "TOP/sector already changed" : "Change planned")}\n" +
            $"Requested by: {row.RequestedBy}\nRequested: {row.RequestedAt:g}\n" +
            $"Assigned IP: {(string.IsNullOrWhiteSpace(row.NewIp) ? "Awaiting assignment" : row.NewIp)}";
    }

    private void TopCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectorCombo == null) return;
        SectorCombo.ItemsSource = _context.Sectors.Where(x => x.Top == (TopCombo.SelectedItem as string))
            .Select(x => x.Sector).Distinct().OrderBy(x => x).ToList();
        SectorCombo.SelectedIndex = -1;
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
        var selected = _context.Sectors.FirstOrDefault(x => x.Top == (TopCombo.SelectedItem as string)
            && x.Sector == (SectorCombo.SelectedItem as string));
        var currentTop = (CurrentTopCombo.SelectedItem as string ?? "").Trim();
        var currentSector = (CurrentSectorCombo.SelectedItem as string ?? "").Trim();
        var currentIp = CurrentIpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(currentTop) || string.IsNullOrWhiteSpace(currentSector))
        { MessageText.Text = "Select the current TOP and sector."; return; }
        if (selected == null || TimingCombo.SelectedIndex < 0)
        { MessageText.Text = "Select a TOP, sector, and field work status."; return; }
        if (MessageBox.Show(this,
            $"Request {currentTop} / {currentSector} → {selected.Top} / {selected.Sector}?\n\nCurrent IP: {currentIp}\n\nDispatch will receive this ticket in TOP Change status.",
            "Confirm TOP Change", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            var saved = await _api.PostAsync<CreateTopChangeRequest, TopChangeDto>($"api/tickets/{_context.TicketId}/top-change",
                new CreateTopChangeRequest
                {
                    ClientRequestId = _requestId, NewSectorId = selected.SectorId,
                    AlreadyChanged = TimingCombo.SelectedIndex == 1, RequestedBy = Actor,
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

    private void CopyRequest_Click(object sender, RoutedEventArgs e) => CopyText(RequestSummaryTextBox.Text);
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
