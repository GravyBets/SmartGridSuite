using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.Tickets;
using SmartGridSuite.Contracts.Snmp;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView : UserControl
    {
        private readonly ApiClient _api;
        private readonly TicketsApi _ticketsApi;
        private CancellationTokenSource? _loadCts;

        private readonly List<SiteDashboardTabSession> _sessions = new();
        private string? _selectedSessionKey;
        private int _blankTabCounter = 1;
        private bool _renderingSession;

        public SiteDashboardPaneView()
            : this(new ApiClient("https://localhost:7140"))
        {
        }

        public SiteDashboardPaneView(ApiClient api)
        {
            InitializeComponent();
            _api = api;
            _ticketsApi = new TicketsApi(_api);

            TopBarView.LoadRequested += TopBarView_LoadRequested;
            TopBarView.AddTabRequested += TopBarView_AddTabRequested;
            TopBarView.SelectedTabChanged += TopBarView_SelectedTabChanged;
            TopBarView.CloseTabRequested += TopBarView_CloseTabRequested;

            WorkspaceView.WriteUpTextChanged += WorkspaceView_WriteUpTextChanged;
            WorkspaceView.SelectedWorkspaceTabChanged += WorkspaceView_SelectedWorkspaceTabChanged;
            WorkspaceView.RefreshTicketRequested += WorkspaceView_RefreshTicketRequested;
            WorkspaceView.RequestCapitalRequested += WorkspaceView_RequestCapitalRequested;
            
            WorkspaceView.RefreshSnmpRequested += WorkspaceView_RefreshSnmpRequested;
            WorkspaceView.RunSelectedSnmpRequested += WorkspaceView_RunSelectedSnmpRequested;

            WorkspaceView.SetSelectedSnmpRequested += WorkspaceView_SetSelectedSnmpRequested;
            WorkspaceView.SnmpTargetChanged += WorkspaceView_SnmpTargetChanged;

            EnsureInitialBlankTab();
            RenderSelectedSession();
        }

        private async void WorkspaceView_RefreshSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            try
            {
                TopBarView.StatusText = "Refreshing SNMP configuration...";
                await RefreshSnmpConfigAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "SNMP configuration refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP configuration refresh failed: {ex.Message}";
            }
        }

        private void WorkspaceView_SnmpTargetChanged(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            session.SnmpTargetIp = WorkspaceView.GetSnmpTargetIp();
        }

        private async void WorkspaceView_SetSelectedSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var selectedOid = WorkspaceView.GetSelectedSnmpOid();
            if (selectedOid is null)
            {
                TopBarView.StatusText = "Select a writable SNMP OID first.";
                return;
            }

            if (!selectedOid.IsWritable)
            {
                TopBarView.StatusText = "Selected OID is read-only.";
                return;
            }

            if (!session.SnmpProfileId.HasValue || session.SnmpProfileId.Value == 0)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            var setValue = WorkspaceView.GetSnmpSetValue();
            if (string.IsNullOrWhiteSpace(setValue))
            {
                TopBarView.StatusText = "Enter a value to set.";
                return;
            }

            try
            {
                TopBarView.StatusText = $"Setting {selectedOid.Label}...";

                var result = await _api.PostAsync<SnmpSetSelectedRequestDto, SnmpSetResultDto>(
                    "api/snmp-profiles/set-selected",
                    new SnmpSetSelectedRequestDto
                    {
                        ProfileId = session.SnmpProfileId.Value,
                        OidId = selectedOid.Id,
                        TargetIp = targetIp,
                        Value = setValue
                    });

                session.SnmpTargetIp = targetIp;
                WorkspaceView.ShowSnmpSetResult(result);

                TopBarView.StatusText = result?.Success == true
                    ? $"SNMP set returned {result.DisplayValue}."
                    : $"SNMP set failed: {result?.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP set failed: {ex.Message}";
                WorkspaceView.ShowSnmpSetResult(new SnmpSetResultDto
                {
                    Success = false,
                    TargetIp = targetIp,
                    RequestedValue = setValue,
                    Label = selectedOid.Label,
                    Oid = selectedOid.Oid,
                    DecodeMode = selectedOid.DecodeMode,
                    ErrorMessage = ex.Message
                });
            }
        }

        private async Task RefreshSnmpConfigAsync(SiteDashboardTabSession session, CancellationToken ct)
        {
            session.SnmpSupported = false;
            session.SnmpDeviceFamily = string.Empty;
            session.SnmpProfileName = string.Empty;
            session.SnmpProfileId = null;
            session.SnmpSupportMessage = string.Empty;
            session.SnmpOids = new List<SnmpOidConfigDto>();

            if (string.IsNullOrWhiteSpace(session.SnmpTargetIp))
                session.SnmpTargetIp = session.PrimaryIp;

            if (string.IsNullOrWhiteSpace(session.SnmpPrimaryCommType))
            {
                session.SnmpSupportMessage = "Primary device type is unavailable.";
                return;
            }

            var supports = await _api.GetAsync<SnmpProfileSupportsSiteDto>(
                $"api/snmp-profiles/supports-site?primaryCommType={Uri.EscapeDataString(session.SnmpPrimaryCommType)}",
                ct);

            if (supports is null || !supports.SnmpSupported || string.IsNullOrWhiteSpace(supports.DeviceFamily))
            {
                session.SnmpSupportMessage = $"SNMP not supported for primary device: {session.SnmpPrimaryCommType}.";
                return;
            }

            var profile = await _api.GetAsync<SnmpProfileDetailDto>(
                $"api/snmp-profiles/active-by-family/{Uri.EscapeDataString(supports.DeviceFamily)}",
                ct);

            if (profile is null)
            {
                session.SnmpSupportMessage = $"No active SNMP profile configured for {supports.DeviceFamily}.";
                return;
            }

            session.SnmpSupported = true;
            session.SnmpDeviceFamily = supports.DeviceFamily;
            session.SnmpProfileName = profile.Name;
            session.SnmpProfileId = profile.Id;
            session.SnmpSupportMessage = $"SNMP ready for {session.HeaderText}.";
            session.SnmpOids = profile.Oids
                .Where(x => x.ShowInWorkspace)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .ToList();
        }

        private async void WorkspaceView_RunSelectedSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var selectedOid = WorkspaceView.GetSelectedSnmpOid();
            if (selectedOid is null)
            {
                TopBarView.StatusText = "Select an SNMP OID first.";
                return;
            }

            if (!session.SnmpProfileId.HasValue || session.SnmpProfileId.Value == 0)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Select or enter a target IP first.";
                return;
            }

            try
            {
                TopBarView.StatusText = $"Running SNMP GET for {selectedOid.Label}...";

                var result = await _api.PostAsync<SnmpRunSelectedRequestDto, SnmpRunResultDto>(
                    "api/snmp-profiles/run-selected",
                    new SnmpRunSelectedRequestDto
                    {
                        ProfileId = session.SnmpProfileId.Value,
                        OidId = selectedOid.Id,
                        TargetIp = targetIp
                    });

                session.SnmpTargetIp = targetIp;
                WorkspaceView.ShowSnmpPollResult(result);

                TopBarView.StatusText = result?.Success == true
                    ? $"SNMP poll returned {result.DisplayValue}."
                    : $"SNMP poll failed: {result?.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP poll failed: {ex.Message}";
                WorkspaceView.ShowSnmpPollResult(new SnmpRunResultDto
                {
                    Success = false,
                    TargetIp = targetIp,
                    Label = selectedOid.Label,
                    Oid = selectedOid.Oid,
                    DecodeMode = selectedOid.DecodeMode,
                    ErrorMessage = ex.Message
                });
            }
        }

        private async void TopBarView_LoadRequested(object? sender, EventArgs e)
        {
            await LoadAsync(TopBarView.SearchText);
        }

        private void TopBarView_AddTabRequested(object? sender, EventArgs e)
        {
            CreateBlankTab(selectNewTab: true);
            RenderSelectedSession();
        }

        private void TopBarView_SelectedTabChanged(object? sender, string? sessionKey)
        {
            _selectedSessionKey = sessionKey;
            RenderSelectedSession();
        }

        private void TopBarView_CloseTabRequested(object? sender, string? sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
                return;

            var index = _sessions.FindIndex(x => x.SessionKey == sessionKey);
            if (index < 0)
                return;

            var wasSelected = string.Equals(_selectedSessionKey, sessionKey, StringComparison.Ordinal);

            _sessions.RemoveAt(index);

            if (_sessions.Count == 0)
            {
                CreateBlankTab(selectNewTab: true);
            }
            else if (wasSelected)
            {
                var newIndex = Math.Min(index, _sessions.Count - 1);
                _selectedSessionKey = _sessions[newIndex].SessionKey;
            }

            RenderSelectedSession();
        }

        private void WorkspaceView_WriteUpTextChanged(object? sender, string text)
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();
            if (session is null)
                return;

            session.WriteUpText = text ?? string.Empty;
        }

        private void WorkspaceView_SelectedWorkspaceTabChanged(object? sender, string? tabKey)
        {
            if (_renderingSession)
                return;

            var session = GetSelectedSession();
            if (session is null)
                return;

            session.SelectedWorkspaceTabKey = string.IsNullOrWhiteSpace(tabKey)
                ? "TopWriteUp"
                : tabKey;
        }

        private async void WorkspaceView_RefreshTicketRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            try
            {
                TopBarView.StatusText = "Refreshing ticket...";
                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Ticket refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Ticket refresh failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_RequestCapitalRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null || session.CurrentTicketId <= 0)
                return;

            try
            {
                TopBarView.StatusText = "Requesting Capital...";
                await _ticketsApi.RequestCapitalAsync(session.CurrentTicketId, CancellationToken.None);
                await RefreshTicketInfoAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                TopBarView.StatusText = "Ticket status changed to Awaiting Capital.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Request Capital failed: {ex.Message}";
            }
        }

        private void EnsureInitialBlankTab()
        {
            if (_sessions.Count > 0)
                return;

            CreateBlankTab(selectNewTab: true);
        }

        private void CreateBlankTab(bool selectNewTab)
        {
            var header = _blankTabCounter == 1 ? "Blank" : $"Blank ({_blankTabCounter})";

            var session = new SiteDashboardTabSession
            {
                SessionKey = Guid.NewGuid().ToString("N"),
                HeaderText = header,
                SearchText = string.Empty
            };

            _blankTabCounter++;
            _sessions.Add(session);

            if (selectNewTab)
                _selectedSessionKey = session.SessionKey;
        }

        private SiteDashboardTabSession? GetSelectedSession()
        {
            if (string.IsNullOrWhiteSpace(_selectedSessionKey))
                return null;

            return _sessions.FirstOrDefault(x => x.SessionKey == _selectedSessionKey);
        }

        private async Task LoadAsync(string rawSiteId)
        {
            var siteId = (rawSiteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                TopBarView.StatusText = "Enter a site ID first.";
                return;
            }

            var existingSession = _sessions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.HeaderText) &&
                !x.HeaderText.StartsWith("Blank", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.HeaderText, siteId, StringComparison.OrdinalIgnoreCase));

            if (existingSession is not null)
            {
                _selectedSessionKey = existingSession.SessionKey;
                RenderSelectedSession();
                TopBarView.StatusText = $"Switched to {existingSession.HeaderText}.";
                return;
            }

            var selectedSession = GetSelectedSession();
            if (selectedSession is null)
            {
                CreateBlankTab(selectNewTab: true);
                selectedSession = GetSelectedSession();
            }

            if (selectedSession is null)
                return;

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();

            try
            {
                TopBarView.SetLoading(true);
                TopBarView.StatusText = $"Loading {siteId}...";

                var dashboard = await _api.GetSiteDashboardAsync(siteId, _loadCts.Token);
                var loadedSiteId = GetObjectPropertyText(dashboard, "SiteId") ?? siteId;

                selectedSession.TicketInfoText = "Loading ticket data...";

                selectedSession.HeaderText = loadedSiteId;
                selectedSession.SearchText = loadedSiteId;
                selectedSession.AddressText = BuildFullAddress(dashboard) ?? "—";
                selectedSession.CoordinatesText = BuildCoordinateSummary(dashboard) ?? "—";

                selectedSession.PrimaryIp = GetDashboardDataFieldText(
                    dashboard,
                    "PrimaryCommunicationsIp",
                    "PrimaryCommIp",
                    "PrimaryCommsIp",
                    "PrimaryCommsIP",
                    "RadioIP",
                    "RadioIp") ?? "—";

                selectedSession.LanIp = GetDashboardDataFieldText(
                    dashboard,
                    "SecondaryLanIp",
                    "SecondaryLanIP",
                    "EthernetIP",
                    "EthernetIp") ?? "—";

                selectedSession.SecondaryIp = GetDashboardDataFieldText(
                    dashboard,
                    "SecondaryWanIp",
                    "SecondaryWanIP",
                    "SecondaryCommunicationsIp",
                    "SecondaryCommsIp",
                    "CellularIP",
                    "CellularIp",
                    "IP1",
                    "WanIp") ?? "—";

                selectedSession.TopInfoText = BuildTopInfoSummary(dashboard);
                selectedSession.SiteStatusText = GetDashboardDataFieldText(dashboard, "SiteStatus", "Status") ?? string.Empty;
                selectedSession.TopAccessTitleText = BuildTopAccessTitle(dashboard);
                selectedSession.EquipmentText = BuildEquipmentSummary(dashboard);
                selectedSession.HistoryRows = BuildHistoryRows(dashboard);

                selectedSession.TicketInfoText = "Loading ticket data...";

                selectedSession.SnmpPrimaryCommType = dashboard?.Route?.PrimaryCommType?.Trim() ?? string.Empty;

                selectedSession.SnmpSupportMessage = "Loading SNMP configuration...";
                selectedSession.SnmpTargetIp = selectedSession.PrimaryIp;

                _selectedSessionKey = selectedSession.SessionKey;
                RenderSelectedSession();

                await RefreshTicketInfoAsync(selectedSession, _loadCts.Token);
                await RefreshSnmpConfigAsync(selectedSession, _loadCts.Token);

                if (selectedSession.SessionKey == _selectedSessionKey)
                    RenderSelectedSession();

                _ = NetworkView.RunQuickReachabilityTestForAllAsync();
                TopBarView.StatusText = $"Loaded {loadedSiteId}.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Load failed: {ex.Message}";
            }
            finally
            {
                TopBarView.SetLoading(false);
            }
        }

        private void RenderSelectedSession()
        {
            EnsureInitialBlankTab();

            TopBarView.SetTabs(_sessions, _selectedSessionKey);

            var session = GetSelectedSession();
            if (session is null)
            {
                _renderingSession = true;
                try
                {
                    TopBarView.ResetHeader();
                    NetworkView.Reset();
                    WorkspaceView.Reset();
                }
                finally
                {
                    _renderingSession = false;
                }

                return;
            }

            _renderingSession = true;

            try
            {
                TopBarView.SetSelectedTab(session.SessionKey);
                TopBarView.SearchText = session.SearchText;
                TopBarView.AddressText = session.AddressText;
                TopBarView.CoordinatesText = session.CoordinatesText;

                if (string.IsNullOrWhiteSpace(TopBarView.StatusText))
                    TopBarView.StatusText = "Ready.";

                NetworkView.Reset();
                NetworkView.SiteHeader = BuildNetworkHeader(session.SiteStatusText, session.HeaderText);
                NetworkView.PrimaryIp = session.PrimaryIp;
                NetworkView.LanIp = session.LanIp;
                NetworkView.SecondaryIp = session.SecondaryIp;

                WorkspaceView.Reset();
                WorkspaceView.TopAccessTitle = session.TopAccessTitleText;
                WorkspaceView.TopInfoText = session.TopInfoText;
                WorkspaceView.TicketInfoText = session.TicketInfoText;
                WorkspaceView.WriteUpText = session.WriteUpText;
                WorkspaceView.EquipmentText = session.EquipmentText;
                WorkspaceView.SetHistoryRows(session.HistoryRows);
                WorkspaceView.SetSelectedWorkspaceTab(session.SelectedWorkspaceTabKey);
                WorkspaceView.CurrentTicketId = session.CurrentTicketId;

                WorkspaceView.SetSnmpContext(
                    session.SnmpSupported,
                    session.SnmpSupportMessage,
                    session.SnmpDeviceFamily,
                    session.SnmpProfileName,
                    session.PrimaryIp,
                    session.LanIp,
                    session.SecondaryIp,
                    session.SnmpTargetIp);

                WorkspaceView.SetSnmpOids(session.SnmpOids);
            }
            finally
            {
                _renderingSession = false;
            }
        }

        private string BuildTicketInfoSummaryFromTickets(IEnumerable<TicketListItemDto>? tickets)
        {
            var ticket = SelectBestTicket(tickets);
            if (ticket is null)
                return "No ticket data returned yet.";

            var lines = new List<string>();

            AddLine(lines, "Notification Name", GetObjectPropertyText(
                ticket,
                "NotificationName",
                "NotificationText",
                "NotificationDescription"));

            AddLine(lines, "Notification #", GetObjectPropertyText(
                ticket,
                "Notification",
                "NotificationNumber",
                "NotificationId"));

            AddLine(lines, "Problem/Issue", GetObjectPropertyText(
                ticket,
                "Problem",
                "Issue"));

            AddLine(lines, "Work Order", GetObjectPropertyText(
                ticket,
                "CurrentWorkOrder",
                "WorkOrder",
                "WorkOrderNumber"));

            AddLine(lines, "Work Order Type", NormalizeTicketWorkOrderType(GetObjectPropertyText(
                ticket,
                "WorkOrderClass",
                "WorkOrderType")));

            AddLine(lines, "Assigned To", GetObjectPropertyText(
                ticket,
                "AssignedTech",
                "Tech"));

            AddLine(lines, "Date Created", FormatTicketCreatedDate(GetObjectPropertyText(
                ticket,
                "CreatedAt",
                "Created")));

            AddLine(lines, "Current Status", GetObjectPropertyText(
                ticket,
                "Status",
                "TicketStatus"));

            return lines.Count == 0
                ? "No ticket data returned yet."
                : string.Join(Environment.NewLine, lines);
        }

        private static string NormalizeTicketWorkOrderType(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.Equals(text, "Cap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Capital", StringComparison.OrdinalIgnoreCase))
                return "Capital";

            if (string.Equals(text, "Maint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Maintenance", StringComparison.OrdinalIgnoreCase))
                return "Maintenance";

            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        private static string FormatTicketCreatedDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            return DateTime.TryParse(value, out var dt)
                ? dt.ToString("MM-dd-yyyy")
                : value.Trim();
        }

        private TicketListItemDto? SelectBestTicket(IEnumerable<TicketListItemDto>? tickets)
        {
            return tickets?
                .OrderByDescending(GetTicketStatusRank)
                .ThenByDescending(t => GetTicketDate(t, "LastActivityAt", "LastActivity"))
                .ThenByDescending(t => GetTicketDate(t, "CreatedAt", "Created"))
                .FirstOrDefault();
        }

        private static int GetTicketStatusRank(TicketListItemDto ticket)
        {
            var status = (GetObjectPropertyText(ticket, "Status", "TicketStatus") ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(status))
                return 1;

            if (status.Equals("Assigned", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("In Progress", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Needs Review", StringComparison.OrdinalIgnoreCase))
                return 2;

            if (status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Canceled", StringComparison.OrdinalIgnoreCase))
                return 0;

            return 1;
        }

        private static DateTime GetTicketDate(TicketListItemDto ticket, params string[] propertyNames)
        {
            var text = GetObjectPropertyText(ticket, propertyNames);
            return DateTime.TryParse(text, out var dt) ? dt : DateTime.MinValue;
        }

        private async Task RefreshTicketInfoAsync(SiteDashboardTabSession session, CancellationToken ct)
        {
            var siteId = session.HeaderText;
            if (string.IsNullOrWhiteSpace(siteId))
            {
                session.CurrentTicketId = 0;
                session.TicketInfoText = "No ticket data returned yet.";
                return;
            }

            var tickets = await _ticketsApi.GetTicketsBySiteAsync(siteId, ct);
            var bestTicket = SelectBestTicket(tickets);

            session.CurrentTicketId = bestTicket?.Id ?? 0;
            session.TicketInfoText = BuildTicketInfoSummaryFromTickets(
                bestTicket is null ? Array.Empty<TicketListItemDto>() : new[] { bestTicket });
        }

        private List<SiteDashboardHistoryRowViewModel> BuildHistoryRows(object? dashboard)
        {
            var result = new List<SiteDashboardHistoryRowViewModel>();

            if (dashboard is null)
                return result;

            var historyItems = FindHistoryEnumerableRecursive(dashboard);
            if (historyItems is null)
                return result;

            foreach (var item in historyItems)
            {
                if (item is null)
                    continue;

                var rawDateText =
                    GetFirstNonEmptyText(item, "VisitDate", "SiteDate", "Date", "CreatedAt");

                var narrative =
                    GetFirstNonEmptyText(item, "Narrative", "Summary", "Notes", "SiteWork")
                    ?? string.Empty;

                var issue =
                    GetFirstNonEmptyText(item, "Issue", "SiteIssue", "Problem")
                    ?? ExtractIssueFromNarrative(narrative)
                    ?? "—";

                var tech1 =
                    GetFirstNonEmptyText(item, "PrimaryTech", "Tech1", "Technician1")
                    ?? "—";

                var tech2 =
                    GetFirstNonEmptyText(item, "SecondaryTech", "Tech2", "Technician2")
                    ?? "—";

                result.Add(new SiteDashboardHistoryRowViewModel(
                    FormatHistoryDate(rawDateText),
                    tech1,
                    tech2,
                    issue,
                    narrative));
            }

            return result;
        }        
        
        private string BuildTopInfoSummary(object? dashboard)
        {
            var lines = new List<string>();

            AddLine(lines, "Site Status", GetDashboardDataFieldText(dashboard, "SiteStatus", "Status"));
            AddLine(lines, "TOP VIP", GetDashboardDataFieldText(dashboard, "TopVip", "TopVIP"));
            AddLine(lines, "TOP IP A", GetDashboardDataFieldText(dashboard, "TopIpA", "TopIPA"));
            AddLine(lines, "TOP IP B", GetDashboardDataFieldText(dashboard, "TopIpB", "TopIPB"));

            return lines.Count == 0
                ? "No TOP fields were returned for this site yet."
                : string.Join(Environment.NewLine, lines);
        }

        private static string BuildNetworkHeader(string? siteStatus, string? siteId)
        {
            var cleanSiteId = string.IsNullOrWhiteSpace(siteId) ? "Site" : siteId.Trim();

            if (string.IsNullOrWhiteSpace(siteStatus))
                return $"Site {cleanSiteId}";

            return $"Site {cleanSiteId} - {siteStatus.Trim()}";
        }

        private string BuildTopAccessTitle(object? dashboard)
        {
            var topName = GetDashboardDataFieldText(dashboard, "TopName", "AssociatedTop", "Top") ?? string.Empty;
            var topDescription = GetDashboardDataFieldText(dashboard, "TopDescription", "TopDescr", "ProductionTop") ?? string.Empty;
            var topSector = GetDashboardDataFieldText(dashboard, "TopSector", "Sector") ?? string.Empty;

            var cleanTopName = topName.Replace("_", "-").Trim();
            var cleanSector = topSector.Trim();
            var cleanDescription = topDescription.Trim();

            var left = cleanTopName;

            if (!string.IsNullOrWhiteSpace(cleanSector))
                left = string.IsNullOrWhiteSpace(left) ? cleanSector : $"{left}-{cleanSector}";

            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(cleanDescription))
                return "TOP Access";

            if (string.IsNullOrWhiteSpace(cleanDescription))
                return left;

            if (string.IsNullOrWhiteSpace(left))
                return $"({cleanDescription})";

            return $"{left} ({cleanDescription})";
        }        

        private string BuildEquipmentSummary(object? dashboard)
        {
            var blocks = new List<string>();

            var enclosure = new List<string>();
            AddLine(enclosure, "Enclosure Model", GetDashboardDataFieldText(dashboard, "EnclosureModel"));
            AddLine(enclosure, "Enclosure SN", GetDashboardDataFieldText(
                dashboard,
                "EnclosureSerialNumber",
                "EnclosureSN",
                "EnclosureSn"));
            if (enclosure.Count > 0)
                blocks.Add(string.Join(Environment.NewLine, enclosure));

            var primary = new List<string>();
            AddLine(primary, "Primary Model", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommType",
                "PrimaryCommunicationsType",
                "PrimaryCommsType"));
            AddLine(primary, "Primary SN", GetDashboardDataFieldText(
                dashboard,
                "PrimaryCommunicationsIdentifier",
                "PrimaryCommsIdentifier",
                "RadioSN",
                "RadioSn"));
            if (primary.Count > 0)
                blocks.Add(string.Join(Environment.NewLine, primary));

            var secondary = new List<string>();
            AddLine(secondary, "Secondary Model", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommType",
                "SecondaryCommunicationsType",
                "SecondaryCommsType"));
            AddLine(secondary, "Secondary SN", GetDashboardDataFieldText(
                dashboard,
                "SecondaryCommunicationsIdentifier",
                "SecondaryCommsIdentifier",
                "ItronCrNum",
                "iTron_CR_Num"));
            if (secondary.Count > 0)
                blocks.Add(string.Join(Environment.NewLine, secondary));

            var antenna = new List<string>();
            AddLine(antenna, "Antenna SN", GetDashboardDataFieldText(
                dashboard,
                "AntennaSerialNumber",
                "AntennaSN",
                "AntennaSn"));
            if (antenna.Count > 0)
                blocks.Add(string.Join(Environment.NewLine, antenna));

            return blocks.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine + Environment.NewLine, blocks);
        }

        private static void AddLine(List<string> lines, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"{label}: {value}");
        }

        private static IEnumerable<object?>? FindHistoryEnumerableRecursive(object source)
        {
            var direct = FindHistoryEnumerableOnUnknown(source);
            if (direct is not null)
                return direct;

            var dataProp = source.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (dataProp is null)
                return null;

            var dataValue = dataProp.GetValue(source);
            return FindHistoryEnumerableOnUnknown(dataValue);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableOnUnknown(object? value)
        {
            if (value is null)
                return null;

            if (value is JsonElement json)
                return FindHistoryEnumerableInJson(json);

            return FindHistoryEnumerableOnObject(value);
        }

        private static IEnumerable<object?>? FindHistoryEnumerableOnObject(object source)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);

                if (value is string || value is not IEnumerable enumerable)
                    continue;

                var elementType = GetEnumerableElementType(prop.PropertyType);

                if (elementType?.Name == "SiteHistoryPreviewDto" ||
                    prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase))
                {
                    return enumerable.Cast<object?>();
                }
            }

            foreach (var prop in props)
            {
                var value = prop.GetValue(source);
                if (value is not JsonElement json)
                    continue;

                var nested = FindHistoryEnumerableInJson(json);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static IEnumerable<object?>? FindHistoryEnumerableInJson(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in json.EnumerateObject())
            {
                if (prop.Name.Contains("history", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.Array)
                {
                    return prop.Value.EnumerateArray()
                        .Select(x => (object?)x.Clone())
                        .ToList();
                }
            }

            foreach (var prop in json.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var nested = FindHistoryEnumerableInJson(prop.Value);
                if (nested is not null)
                    return nested;
            }

            return null;
        }

        private static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                if (args.Length == 1)
                    return args[0];
            }

            var enumerableInterface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                     i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableInterface?.GetGenericArguments().FirstOrDefault();
        }

        private static string? GetFirstNonEmptyText(object source, params string[] propertyNames)
        {
            if (source is JsonElement json)
                return FirstNonEmptyJsonProperty(json, propertyNames);

            return FirstNonEmptyObjectProperty(source, propertyNames);
        }

        private static string? FirstNonEmptyObjectProperty(object source, params string[] propertyNames)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var candidate in propertyNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                var prop = props.FirstOrDefault(p => NormalizeToken(p.Name) == normalizedCandidate);
                if (prop is null)
                    continue;

                var value = prop.GetValue(source);
                if (value is null)
                    continue;

                var text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private static string? FirstNonEmptyJsonProperty(JsonElement json, params string[] propertyNames)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in json.EnumerateObject())
            {
                var normalizedActual = NormalizeToken(prop.Name);

                foreach (var candidate in propertyNames)
                {
                    if (normalizedActual != NormalizeToken(candidate))
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private static string NormalizeToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Trim()
                .ToUpperInvariant();
        }

        private static object? GetDashboardDataValue(object? dashboard)
        {
            if (dashboard is null)
                return null;

            var dataProp = dashboard.GetType().GetProperty(
                "Data",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            return dataProp?.GetValue(dashboard);
        }

        private static object? GetRouteValue(object? dashboard)
        {
            if (dashboard is null)
                return null;

            var routeProp = dashboard.GetType().GetProperty(
                "Route",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            return routeProp?.GetValue(dashboard);
        }

        private static string? GetObjectPropertyText(object? source, params string[] propertyNames)
        {
            if (source is null)
                return null;

            return GetFirstNonEmptyText(source, propertyNames);
        }

        private string? GetDashboardDataFieldText(object? dashboard, params string[] candidateNames)
        {
            var data = GetDashboardDataValue(dashboard);
            if (data is null)
                return null;

            if (data is JsonElement json)
                return GetDashboardDataFieldTextFromJson(json, candidateNames);

            return GetDashboardDataFieldTextFromObject(data, candidateNames);
        }

        private string? GetDashboardDataFieldTextFromObject(object source, params string[] candidateNames)
        {
            var props = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var candidate in candidateNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                var prop = props.FirstOrDefault(p => NormalizeToken(p.Name) == normalizedCandidate);
                if (prop is null)
                    continue;

                var value = prop.GetValue(source);
                if (value is null)
                    continue;

                var text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private string? GetDashboardDataFieldTextFromJson(JsonElement json, params string[] candidateNames)
        {
            if (json.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var candidate in candidateNames)
            {
                var normalizedCandidate = NormalizeToken(candidate);

                foreach (var prop in json.EnumerateObject())
                {
                    if (NormalizeToken(prop.Name) != normalizedCandidate)
                        continue;

                    var text = prop.Value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        private string? BuildFullAddress(object? dashboard)
        {
            var direct = GetDashboardDataFieldText(
                dashboard,
                "FullAddress",
                "Address",
                "StreetAddress",
                "FormattedAddress");

            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            var streetNo = GetDashboardDataFieldText(dashboard, "StreetNo");
            var streetName = GetDashboardDataFieldText(dashboard, "StreetName");
            var city = GetDashboardDataFieldText(dashboard, "City");
            var state = GetDashboardDataFieldText(dashboard, "State", "StateCode");
            var zip = GetDashboardDataFieldText(dashboard, "Zip", "ZipCode");

            var line1 = string.Join(" ", new[] { streetNo, streetName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var cityState = string.Join(", ", new[] { city, state }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var line2 = string.Join(" ", new[] { cityState, zip }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            var combined = string.Join("  ", new[] { line1, line2 }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }

        private string? BuildCoordinateSummary(object? dashboard)
        {
            var latitude = GetDashboardDataFieldText(dashboard, "Latitude", "Lat");
            var longitude = GetDashboardDataFieldText(dashboard, "Longitude", "Lon", "Lng");

            if (string.IsNullOrWhiteSpace(latitude) && string.IsNullOrWhiteSpace(longitude))
                return null;

            if (string.IsNullOrWhiteSpace(latitude))
                return longitude;

            if (string.IsNullOrWhiteSpace(longitude))
                return latitude;

            return $"{latitude}, {longitude}";
        }

        private static string? ExtractIssueFromNarrative(string? narrative)
        {
            if (string.IsNullOrWhiteSpace(narrative))
                return null;

            var lines = narrative
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var line in lines)
            {
                if (line.StartsWith("Site Issue:", StringComparison.OrdinalIgnoreCase))
                    return line["Site Issue:".Length..].Trim();
            }

            return lines.FirstOrDefault();
        }

        private static string FormatHistoryDate(string? rawDateText)
        {
            if (string.IsNullOrWhiteSpace(rawDateText))
                return "—";

            if (DateTime.TryParse(rawDateText, out var dt))
                return dt.ToString("MM-dd-yyyy");

            return rawDateText.Trim();
        }
    }
}