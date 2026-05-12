using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using System.Windows;
using System.Windows.Controls;

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
            WriteUpTextBox.TextChanged += WriteUpTextBox_TextChanged;

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

        public string CurrentCnpTechName { get; set; } = string.Empty;
                
        public Func<string>? PingStatsProvider { get; set; }

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
            set => WriteUpTextBox.Text = value ?? string.Empty;
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

            _snmpCategoryOptionsInitialized = false;

            IncludeSnmpStatsCheckBox.IsChecked = false;
            IncludeSnmpAdminCheckBox.IsChecked = true;
            IncludeSnmpConfigCheckBox.IsChecked = true;
            IncludeSnmpStatsCategoryCheckBox.IsChecked = true;
            SnmpCategoryOptionsPanel.Visibility = Visibility.Collapsed;

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
    }
}