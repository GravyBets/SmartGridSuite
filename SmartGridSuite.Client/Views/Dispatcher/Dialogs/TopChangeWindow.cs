using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public sealed class TopChangeWindow : Window
{
    private readonly ApiClient _api = ClientAppSettings.CreateApiClient();
    private readonly TopChangeContextDto _context;
    private readonly StackPanel _body = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
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

    private TopChangeWindow(TopChangeContextDto context, bool dispatch)
    {
        _context = context;
        Title = $"TOP Change — {context.Site}";
        Width = 620; Height = 620; MinWidth = 500; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        _body.Margin = new Thickness(20);
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AddText($"Site: {context.Site}   •   Ticket: {context.TicketId}", true);
        AddText($"Current TOP: {context.CurrentTop}\nCurrent sector: {context.CurrentSector}\nCurrent IP: {context.CurrentIp}");
        if (!string.IsNullOrWhiteSpace(context.DataWarning)) AddText(context.DataWarning);

        if (context.Request is { State: not "Completed" } row)
            BuildExisting(row, dispatch);
        else if (dispatch)
            AddText("There is no active TOP-change request on this ticket.");
        else
            BuildNew();

        _body.Children.Add(_message);
        var close = Button("Close", (_, _) => Close());
        close.IsCancel = true;
        _body.Children.Add(close);
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }

    private void BuildNew()
    {
        AddText("New TOP", true);
        var top = Combo();
        top.ItemsSource = _context.Sectors.Select(x => x.Top).Distinct().OrderBy(x => x).ToList();
        _body.Children.Add(top);
        AddText("New sector", true);
        var sector = Combo();
        sector.DisplayMemberPath = nameof(TopChangeSectorOption.Sector);
        _body.Children.Add(sector);
        top.SelectionChanged += (_, _) =>
        {
            sector.ItemsSource = _context.Sectors.Where(x => x.Top == (top.SelectedItem as string)).ToList();
            sector.SelectedIndex = -1;
        };
        AddText("Field work status", true);
        var timing = Combo();
        timing.ItemsSource = new[] { "Change planned — waiting for IP", "TOP/sector already changed — waiting for IP" };
        _body.Children.Add(timing);
        if (_context.Sectors.Count == 0)
            AddText("No TOP sectors are available. Refresh the server's tower cache before requesting a change.");
        _body.Children.Add(Button("Submit TOP Change", async (_, _) =>
        {
            if (sector.SelectedItem is not TopChangeSectorOption selected || timing.SelectedIndex < 0)
            { _message.Text = "Select a TOP, sector, and field work status."; return; }
            if (MessageBox.Show(this,
                $"Request {_context.CurrentTop} / {_context.CurrentSector} → {selected.Top} / {selected.Sector}?\n\nDispatch will receive this ticket in TOP Change status.",
                "Confirm TOP Change", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await RunAsync(async () =>
            {
                var saved = await _api.PostAsync<CreateTopChangeRequest, TopChangeDto>($"api/tickets/{_context.TicketId}/top-change",
                    new CreateTopChangeRequest
                    {
                        ClientRequestId = _requestId, NewSectorId = selected.SectorId,
                        AlreadyChanged = timing.SelectedIndex == 1, RequestedBy = Actor,
                        ExpectedTop = _context.CurrentTop, ExpectedSector = _context.CurrentSector, ExpectedIp = _context.CurrentIp
                    });
                if (saved == null) throw new InvalidOperationException("No response received. Reopen the request to check whether it was saved.");
                _message.Text = "Request saved. Dispatch is awaiting an IP for this TOP change.";
            }, closeOnSuccess: true);
        }));
    }

    private void BuildExisting(TopChangeDto row, bool dispatch)
    {
        AddText(row.State == "IpReady" ? "New IP Ready" : "Waiting for IP", true);
        AddText($"New TOP: {row.NewTop}\nNew sector: {row.NewSector}\nRequested: {row.RequestedAt:g}\n" +
            (row.AlreadyChanged ? "TOP/sector was already changed when requested." : "TOP/sector change was planned when requested."));
        var ip = new TextBox { Text = row.NewIp, IsReadOnly = !dispatch, Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(8) };
        if (TryFindResource("ModernTextBox") is Style textStyle) ip.Style = textStyle;
        AddText("Assigned IP", true);
        _body.Children.Add(ip);
        if (row.State == "IpReady")
        {
            _body.Children.Add(Button("Copy IP", (_, _) => Clipboard.SetText(row.NewIp)));
            AddText("Include this work in your normal site write-up when finished.");
        }
        AddText(row.EmailStatus);
        if (!dispatch) return;

        _body.Children.Add(Button("Save IP / Notify Technician", async (_, _) =>
        {
            await RunAsync(async () =>
            {
                var saved = await _api.PostAsync<AssignTopChangeIpRequest, TopChangeDto>(
                    $"api/tickets/{row.TicketId}/top-change/{row.Id}/assign-ip",
                    new AssignTopChangeIpRequest { Ip = ip.Text.Trim(), AssignedBy = Actor });
                if (saved == null) throw new InvalidOperationException("No response received. Refresh before retrying.");
                _message.Text = $"IP saved. {saved.EmailStatus}";
                if (saved != null) MessageBox.Show(this, _message.Text, "TOP Change");
            }, closeOnSuccess: true);
        }));
        if (row.SiteKind != "AMS" && row.State == "PendingIp")
            _body.Children.Add(Button("Email IP Request", async (_, _) =>
            {
                if (MessageBox.Show(this, "Send this TOP-change request to the configured IP-assignment team?",
                    "Email IP Request", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                await RunAsync(async () =>
                {
                    var result = await _api.PostAsync<object, TopChangeEmailResult>(
                        $"api/tickets/{row.TicketId}/top-change/{row.Id}/email-request", new { });
                    _message.Text = $"{result?.Status}: {result?.Message}";
                });
            }));
    }

    private async Task RunAsync(Func<Task> action, bool closeOnSuccess = false)
    {
        if (_saving) return;
        _saving = true; _body.IsEnabled = false; _message.Text = "Saving...";
        var success = false;
        try { await action(); success = true; }
        catch (Exception ex) { _message.Text = "Request failed. Your entries remain available to retry."; ShowError(ex); }
        finally { _saving = false; _body.IsEnabled = true; }
        if (success && closeOnSuccess) Close();
    }
    private void AddText(string text, bool heading = false) => _body.Children.Add(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6),
        FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal
    });
    private ComboBox Combo()
    {
        var combo = new ComboBox { MinHeight = 30, Margin = new Thickness(0, 0, 0, 6) };
        if (TryFindResource("ModernComboBoxStyle") is Style style) combo.Style = style;
        return combo;
    }
    private Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, MinHeight = 32, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        if (TryFindResource("SecondaryButtonStyle") is Style style) button.Style = style;
        button.Click += click;
        return button;
    }
    private static void ShowError(Exception ex) => MessageBox.Show(
        ex is ApiClient.ApiException api ? api.Body ?? api.Message : ex.Message,
        "TOP Change", MessageBoxButton.OK, MessageBoxImage.Warning);
}
