using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.SiteNotes;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public sealed class SnmpRunOidRequestedEventArgs : EventArgs
    {
        public SnmpRunOidRequestedEventArgs(SnmpOidConfigDto oid)
        {
            Oid = oid;
        }

        public SnmpOidConfigDto Oid { get; }
    }

    public sealed class SnmpRunCategoryRequestedEventArgs : EventArgs
    {
        public SnmpRunCategoryRequestedEventArgs(string category, IReadOnlyList<SnmpOidConfigDto> oids)
        {
            Category = category;
            Oids = oids;
        }

        public string Category { get; }
        public IReadOnlyList<SnmpOidConfigDto> Oids { get; }
    }

    public partial class SiteDashboardWorkspaceView : UserControl
    {
        public SiteDashboardWorkspaceView()
        {
            InitializeComponent();

            Loaded += SiteDashboardWorkspaceView_Loaded;

            _writeUpTextChangedDebounceTimer.Tick += WriteUpTextChangedDebounceTimer_Tick;

            SiteNotesItemsControl.ItemsSource = _siteNotes;
            RxAssociatedSitesItemsControl.ItemsSource = _rxAssociatedSiteResults;

            Reset();
        }

        public event EventHandler<string>? WriteUpTextChanged;
        public event EventHandler<string?>? SelectedWorkspaceTabChanged;
        public event EventHandler? RefreshTicketRequested;
        public event EventHandler<TicketActionRequestedEventArgs>? TicketActionRequested;
        public event EventHandler<WriteUpSubmitRequestedEventArgs>? WriteUpSubmitRequested;
        public event EventHandler<string>? RxIpLookupRequested;
        public event EventHandler<string>? OpenAssociatedSiteRequested;
        public event EventHandler? RefreshSnmpRequested;
        public event EventHandler? SetSelectedSnmpRequested;
        public event EventHandler? SnmpTargetChanged;
        public event EventHandler? SelectedSnmpProfileChanged;
        public event EventHandler? OpenTopTunnelRequested;

        private readonly List<TowerSectorPingCard> _towerPingCards = new();

        private CancellationTokenSource? _towerTestAllCts;

        private readonly ObservableCollection<SiteNoteDto> _siteNotes = new();
        private readonly SiteNotesApi _siteNotesApi = new(ClientAppSettings.CreateApiClient());
        private int _siteNotesLoadVersion;

        public static readonly DependencyProperty CanManageSiteNotesProperty = DependencyProperty.Register(
            nameof(CanManageSiteNotes),
            typeof(bool),
            typeof(SiteDashboardWorkspaceView),
            new PropertyMetadata(true));

        public bool CanManageSiteNotes
        {
            get => (bool)GetValue(CanManageSiteNotesProperty);
            set => SetValue(CanManageSiteNotesProperty, value);
        }

        public string CurrentSiteId { get; private set; } = "";

        public string CurrentCnpTechName { get; set; } = string.Empty;
                
        public Func<string>? PingStatsProvider { get; set; }

        public Func<IReadOnlyList<string>>? IpChangeWriteUpLinesProvider { get; set; }

        public long CurrentTicketId { get; set; }

        public string TowerSummaryText
        {
            get => _towerSummaryText;
            set
            {
                _towerSummaryText = value ?? string.Empty;
                RefreshTowerHeaderDisplay();
            }
        }
        private string _towerSummaryText = string.Empty;

        public string TopAccessTitle
        {
            get => TopAccessTitleTextBlock.ToolTip?.ToString()
                   ?? TopAccessTitleTextBlock.Text;

            set
            {
                var fullTitle = string.IsNullOrWhiteSpace(value)
                    ? "TOP Access"
                    : value.Trim();

                TopAccessTitleTextBlock.Text = GetShortTopAccessTitle(fullTitle);
                TopAccessTitleTextBlock.ToolTip = fullTitle;
            }
        }

        public string TopInfoText
        {
            get => TopInfoTextBox.Text;
            set
            {
                TopInfoTextBox.Text = value ?? string.Empty;
                RefreshTopAccessPanel();
                RefreshRangeExtenderPanel();
            }
        }

        public string TopTunnelIp
        {
            get => TopTunnelIpTextBox.Text;
            set
            {
                var hasValue = !string.IsNullOrWhiteSpace(value) && value.Trim() != "—";

                TopTunnelIpTextBox.Text = hasValue ? value.Trim() : string.Empty;
                TopTunnelRow.Visibility = hasValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public string EquipmentDashboardKind
        {
            get => _equipmentDashboardKind;
            set
            {
                _equipmentDashboardKind = value ?? string.Empty;
                ApplyDashboardFeatureVisibility();
                RefreshEquipmentCards();
            }
        }
        private string _equipmentDashboardKind = string.Empty;

        public string TicketInfoText
        {
            get => _ticketInfoText;
            set
            {
                _ticketInfoText = value ?? string.Empty;
                ApplyTicketInfo(_ticketInfoText);
            }
        }
        private string _ticketInfoText = string.Empty;

        private bool _syncingWorkspaceTab;

        public string WriteUpText
        {
            get => WriteUpTextBox.Text;
            set
            {
                var newValue = value ?? string.Empty;

                if (string.Equals(WriteUpTextBox.Text, newValue, StringComparison.Ordinal))
                    return;

                _suppressWriteUpTextChanged = true;

                try
                {
                    WriteUpTextBox.Text = newValue;
                    _pendingWriteUpText = newValue;
                }
                finally
                {
                    _suppressWriteUpTextChanged = false;
                }
            }
        }

        public string EquipmentText
        {
            get => _equipmentText;
            set
            {
                _equipmentText = value ?? string.Empty;
                RefreshEquipmentCards();
                
            }
        }

        public string SelectedWorkspaceTabKey
        {
            get
            {
                if (WorkspaceTabControl.SelectedItem is TabItem item)
                    return item.Tag?.ToString() ?? "TopWriteUp";

                return "TopWriteUp";
            }
        }

        public bool ShowPortalTab
        {
            get => PortalTabItem.Visibility == Visibility.Visible;
            set => PortalTabItem.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public string PortalUrl
        {
            get => _portalUrl;
            set => _portalUrl = value ?? string.Empty;
        }

        private readonly DispatcherTimer _writeUpTextChangedDebounceTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        private string _pendingWriteUpText = string.Empty;
        private bool _suppressWriteUpTextChanged;

        public void Reset()
        {
            StopTowerPings();

            // Main workspace text/state
            TopInfoText = string.Empty;
            TopAccessTitle = "TOP Access";
            WriteUpText = string.Empty;
            TicketInfoText = string.Empty;
            TopTunnelIp = "—";
            CurrentTicketId = 0;

            CurrentSiteId = "";
            _siteNotes.Clear();
            RefreshSiteNotesEmptyState();

            _snmpCategoryOptionsInitialized = false;

            IncludePingStatsCheckBox.IsChecked = true;
            IncludeSnmpStatsCheckBox.IsChecked = false;
            IncludeSnmpAdminCheckBox.IsChecked = true;
            IncludeSnmpConfigCheckBox.IsChecked = true;
            IncludeSnmpStatsCategoryCheckBox.IsChecked = true;
            SnmpCategoryOptionsPanel.Visibility = Visibility.Collapsed;

            ClearWriteUpWorkflowSelections();

            //Tower
            TowerSummaryText = string.Empty;
            SetTowerSectors(Array.Empty<TowerSectorDto>());

            // History
            SetHistoryRows(Array.Empty<SiteDashboardHistoryRowViewModel>());

            // Portal
            ShowPortalTab = false;
            PortalUrl = string.Empty;
            _lastPortalRequestedUrl = string.Empty;

            // Equipment
            EquipmentDashboardKind = string.Empty;
            _showSensitiveEquipmentValues = false;

            if (ToggleSensitiveEquipmentButton is not null)
                ToggleSensitiveEquipmentButton.Content = "View";

            SerializedDevicesPanel.Children.Clear();
            AccessSecuritySectionPanel.Children.Clear();
            ReplacementEntriesPanel.Children.Clear();
            _activeReplacementEntryKeys.Clear();

            _equipmentText = string.Empty;
            RefreshEquipmentCards();

            // Workspace tab
            SetSelectedWorkspaceTab("TopWriteUp");

            // SNMP
            ResetSnmp();
        }

        public async Task LoadSiteNotesAsync(string siteId, CancellationToken ct = default)
        {
            var requestedSiteId = (siteId ?? string.Empty).Trim().ToUpperInvariant();

            CurrentSiteId = requestedSiteId;

            var loadVersion = ++_siteNotesLoadVersion;

            _siteNotes.Clear();
            RefreshSiteNotesEmptyState();

            if (string.IsNullOrWhiteSpace(requestedSiteId))
                return;

            try
            {
                var notes = await _siteNotesApi.GetBySiteAsync(requestedSiteId, ct);

                if (ct.IsCancellationRequested)
                    return;

                if (loadVersion != _siteNotesLoadVersion)
                    return;

                if (!string.Equals(CurrentSiteId, requestedSiteId, StringComparison.OrdinalIgnoreCase))
                    return;

                _siteNotes.Clear();

                foreach (var note in notes
                             .GroupBy(x => x.Id)
                             .Select(g => g.First())
                             .OrderBy(x => x.NoteType)
                             .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt))
                {
                    _siteNotes.Add(note);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (loadVersion != _siteNotesLoadVersion)
                    return;

                MessageBox.Show(
                    $"Failed to load site notes.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            RefreshSiteNotesEmptyState();
        }

        private void RefreshSiteNotesEmptyState()
        {
            var count = _siteNotes.Count;

            if (SiteNotesEmptyTextBlock != null)
            {
                SiteNotesEmptyTextBlock.Visibility = count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (SiteNotesCountBadge != null)
            {
                SiteNotesCountBadge.Visibility = count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (SiteNotesCountTextBlock != null)
            {
                SiteNotesCountTextBlock.Text = count.ToString();
            }
        }

        private string GetCurrentUserDisplayName()
        {
            var currentName = (CurrentCnpTechName ?? string.Empty).Trim();

            if (!IsGenericUserName(currentName))
                return currentName;

            var fullName = Environment.GetEnvironmentVariable("FULLNAME")?.Trim();

            if (!IsGenericUserName(fullName))
                return fullName!;

            var windowsName = WindowsIdentity.GetCurrent()?.Name;

            if (!string.IsNullOrWhiteSpace(windowsName))
            {
                var cleanWindowsName = windowsName.Trim();

                var slashIndex = cleanWindowsName.LastIndexOf('\\');
                if (slashIndex >= 0 && slashIndex < cleanWindowsName.Length - 1)
                    cleanWindowsName = cleanWindowsName[(slashIndex + 1)..];

                if (!IsGenericUserName(cleanWindowsName))
                    return cleanWindowsName;
            }

            return string.IsNullOrWhiteSpace(Environment.UserName)
                ? "Unknown"
                : Environment.UserName;
        }

        private static bool IsGenericUserName(string? value)
        {
            var clean = (value ?? string.Empty).Trim();

            return string.IsNullOrWhiteSpace(clean)
                || clean.Equals("Dispatcher", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Dispatch", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        }

        private async void AddSiteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentSiteId))
            {
                MessageBox.Show(
                    "Load a site before adding a site note.",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var win = new SiteNoteEditorWindow(CurrentSiteId)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            try
            {
                await _siteNotesApi.CreateAsync(new CreateSiteNoteRequest
                {
                    SiteId = CurrentSiteId,
                    NoteType = win.NoteType,
                    NoteText = win.NoteText,
                    CreatedBy = GetCurrentUserDisplayName()
                });

                await LoadSiteNotesAsync(CurrentSiteId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to add site note.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void EditSiteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SiteNoteDto note)
                return;

            var win = new SiteNoteEditorWindow(CurrentSiteId, note)
            {
                Owner = Window.GetWindow(this)
            };

            if (win.ShowDialog() != true)
                return;

            try
            {
                await _siteNotesApi.UpdateAsync(new UpdateSiteNoteRequest
                {
                    Id = note.Id,
                    NoteType = win.NoteType,
                    NoteText = win.NoteText,
                    UpdatedBy = GetCurrentUserDisplayName()
                });

                await LoadSiteNotesAsync(CurrentSiteId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to update site note.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void DeleteSiteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SiteNoteDto note)
                return;

            var confirm = MessageBox.Show(
                "Delete this site note?",
                "Delete Site Note",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                await _siteNotesApi.DeleteAsync(note.Id, GetCurrentUserDisplayName());

                await LoadSiteNotesAsync(CurrentSiteId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to delete site note.\n\n{ex.Message}",
                    "Site Notes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public SiteDashboardSubmitOptionsSessionState GetSubmitOptionsSessionState()
        {
            return new SiteDashboardSubmitOptionsSessionState
            {
                IncludePingStats = IncludePingStatsCheckBox.IsChecked == true,

                IncludeSnmpStats = IncludeSnmpStatsCheckBox.IsChecked == true,
                IncludeSnmpAdmin = IncludeSnmpAdminCheckBox.IsChecked == true,
                IncludeSnmpConfig = IncludeSnmpConfigCheckBox.IsChecked == true,
                IncludeSnmpStatsCategory = IncludeSnmpStatsCategoryCheckBox.IsChecked == true,

                IncludeReferTo = IncludeReferToCheckBox.IsChecked == true,

                WriteUpFlagIds =
                    GetSelectedWriteUpFlagIds()
                        .ToList(),

                ReferToOptionIds =
                    GetSelectedReferToOptionIds()
                        .ToList()
            };
        }

        public void RestoreSubmitOptionsSessionState(SiteDashboardSubmitOptionsSessionState? state)
        {
            state ??= new SiteDashboardSubmitOptionsSessionState();

            IncludePingStatsCheckBox.IsChecked = state.IncludePingStats;

            IncludeSnmpStatsCheckBox.IsChecked = state.IncludeSnmpStats;
            IncludeSnmpAdminCheckBox.IsChecked = state.IncludeSnmpAdmin;
            IncludeSnmpConfigCheckBox.IsChecked = state.IncludeSnmpConfig;
            IncludeSnmpStatsCategoryCheckBox.IsChecked = state.IncludeSnmpStatsCategory;

            SnmpCategoryOptionsPanel.Visibility = state.IncludeSnmpStats
                ? Visibility.Visible
                : Visibility.Collapsed;

            IncludeReferToCheckBox.IsChecked =
                state.IncludeReferTo ||
                state.ReferToOptionIds?.Count > 0;

            RestoreWriteUpWorkflowSelections(
                state.WriteUpFlagIds,
                state.ReferToOptionIds);
        }
    }
}