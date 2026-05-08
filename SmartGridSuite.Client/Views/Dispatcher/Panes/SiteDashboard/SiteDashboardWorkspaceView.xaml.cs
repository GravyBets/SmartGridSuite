using Microsoft.Web.WebView2.Core;
using SmartGridSuite.Contracts.Settings;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        public event EventHandler<string>? WriteUpTextChanged;
        public event EventHandler<string?>? SelectedWorkspaceTabChanged;

        public event EventHandler? RefreshTicketRequested;
        public event EventHandler<TicketActionRequestedEventArgs>? TicketActionRequested;

        public event EventHandler<WriteUpSubmitRequestedEventArgs>? WriteUpSubmitRequested;

        public event EventHandler<string>? RxIpLookupRequested;
        public event EventHandler<string>? OpenAssociatedSiteRequested;

        public string CurrentCnpTechName { get; set; } = string.Empty;

        private string _rxAssociatedSiteId = string.Empty;
                
        public Func<string>? PingStatsProvider { get; set; }

        public event EventHandler? RefreshSnmpRequested;
        
        public event EventHandler? SetSelectedSnmpRequested;
        public event EventHandler? SnmpTargetChanged;
        public event EventHandler? SelectedSnmpProfileChanged;

        private bool _syncingSnmpProfileCombo;
        private bool _syncingSnmpTargetCombo;
        private bool _syncingWritableOidCombo;
        private string? _snmpPrimaryIp;
        private string? _snmpLanIp;
        private string? _snmpSecondaryIp;

        private string _towerSummaryText = string.Empty;

        public string TowerSummaryText
        {
            get => _towerSummaryText;
            set
            {
                _towerSummaryText = value ?? string.Empty;
                RefreshTowerHeaderDisplay();
            }
        }

        public event EventHandler<SnmpRunOidRequestedEventArgs>? RunSnmpOidRequested;
        public event EventHandler<SnmpRunCategoryRequestedEventArgs>? RunSnmpCategoryRequested;

        public event EventHandler? OpenTopTunnelRequested;

        private List<SnmpCategoryGroupViewModel> _snmpCategoryGroups = new();

        private bool _portalInitialized;
        private string _portalUrl = string.Empty;

        private string _lastPortalRequestedUrl = string.Empty;

        private string _equipmentDashboardKind = string.Empty;

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

        //Tower Fields
        private sealed class TowerSectorPingCard
        {
            public string Sector { get; set; } = "";

            public Border? CardBorder { get; set; }

            public TextBox? PingCountTextBox { get; set; }

            public CancellationTokenSource? PingCts { get; set; }
            public bool IsRunning { get; set; }

            public List<TowerPingEndpoint> Endpoints { get; set; } = new();
        }

        private sealed class TowerPingEndpoint
        {
            public TowerSectorPingCard? ParentSector { get; set; }

            public string Label { get; set; } = "";
            public string IpAddress { get; set; } = "";

            public TextBox? IpTextBox { get; set; }
            public Brush? DefaultIpBorderBrush { get; set; }
            public Brush? DefaultIpBackground { get; set; }
            public Brush? DefaultIpForeground { get; set; }

            public TextBox? ResultTextBox { get; set; }
            public TextBlock? SummaryTextBlock { get; set; }

            public bool IsRunning { get; set; }
        }

        private readonly List<TowerSectorPingCard> _towerPingCards = new();

        public long CurrentTicketId { get; set; }

        private bool _syncingWorkspaceTab;

        private List<CommunicationDeviceTypeDto> _communicationDeviceTypes = new();
        
        private static readonly string[] FallbackCommunicationDeviceTypes =
        {
            "Radio",
            "PMR",
            "LTE Modem",
            "Cell Modem",
            "AP",
            "Router",
            "Other"
        };

        public sealed class WriteUpSubmitRequestedEventArgs : EventArgs
        {
            public WriteUpSubmitRequestedEventArgs(
                string finalWriteUpText,
                bool includeEquipmentReplacements,
                bool includePingStats,
                bool includeSnmpStats)
            {
                FinalWriteUpText = finalWriteUpText;
                IncludeEquipmentReplacements = includeEquipmentReplacements;
                IncludePingStats = includePingStats;
                IncludeSnmpStats = includeSnmpStats;
            }

            public string FinalWriteUpText { get; }
            public bool IncludeEquipmentReplacements { get; }
            public bool IncludePingStats { get; }
            public bool IncludeSnmpStats { get; }
        }

        private sealed class ReplacementEntryRowTag
        {
            public string Label { get; set; } = string.Empty;
            public bool UsesCommunicationDeviceTypePicker { get; set; }
            public string? ReplacementKey { get; set; }
        }

        private sealed class EquipmentReplacementWriteUpEntry
        {
            public string SlotLabel { get; set; } = string.Empty;
            public string Item { get; set; } = string.Empty;
            public string OldSerial { get; set; } = string.Empty;
            public string NewSerial { get; set; } = string.Empty;
            public bool UsesCommunicationDeviceTypePicker { get; set; }
        }

        public SiteDashboardWorkspaceView()
        {
            InitializeComponent();
            WriteUpTextBox.TextChanged += WriteUpTextBox_TextChanged;
            
            Reset();
        }

        private static readonly Regex IpRegex =
            new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);

        private string _ticketInfoText = string.Empty;

        public string TicketInfoText
        {
            get => _ticketInfoText;
            set
            {
                _ticketInfoText = value ?? string.Empty;
                ApplyTicketInfo(_ticketInfoText);
            }
        }

        private string _equipmentText = string.Empty;

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

        private bool _showSensitiveEquipmentValues;

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


        private string _rangeExtenderLinkUrl = string.Empty;

        public string RangeExtenderLinkUrl
        {
            get => _rangeExtenderLinkUrl;
            set => _rangeExtenderLinkUrl = value ?? string.Empty;
        }

        private void ApplyWorkspaceTabVisualState(string? tabKey, bool raiseChangedEvent)
        {
            var resolved = string.IsNullOrWhiteSpace(tabKey)
                ? "TopWriteUp"
                : tabKey.Trim();

            // If Portal was requested but this site should not show it, fall back to Main
            if (string.Equals(resolved, "Portal", StringComparison.OrdinalIgnoreCase) && !ShowPortalTab)
                resolved = "TopWriteUp";

            if (string.Equals(resolved, "RxOverview", StringComparison.OrdinalIgnoreCase) &&
                RxOverviewTabItem.Visibility != Visibility.Visible)
            {
                resolved = "TopWriteUp";
            }

            if (string.Equals(resolved, "SNMPTool", StringComparison.OrdinalIgnoreCase) &&
                SnmpTabItem.Visibility != Visibility.Visible)
            {
                resolved = IsRangeExtenderDashboard ? "RxOverview" : "TopWriteUp";
            }

            if (string.Equals(resolved, "TowerOverview", StringComparison.OrdinalIgnoreCase) &&
                TowerOverviewTabItem.Visibility != Visibility.Visible)
            {
                resolved = "TopWriteUp";
            }

            if (!string.Equals(resolved, "TowerOverview", StringComparison.OrdinalIgnoreCase))
                StopTowerPings();

            TopWriteUpPanel.Visibility = string.Equals(resolved, "TopWriteUp", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            TowerOverviewPanel.Visibility = string.Equals(resolved, "TowerOverview", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            RxOverviewPanel.Visibility = string.Equals(resolved, "RxOverview", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            PortalPanel.Visibility = string.Equals(resolved, "Portal", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            SiteHistoryPanel.Visibility = string.Equals(resolved, "SiteHistory", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            EquipmentPanel.Visibility = string.Equals(resolved, "Equipment", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            SnmpPanel.Visibility = string.Equals(resolved, "SNMPTool", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (string.Equals(resolved, "Portal", StringComparison.OrdinalIgnoreCase))
                _ = NavigatePortalAsync();

            if (raiseChangedEvent)
                SelectedWorkspaceTabChanged?.Invoke(this, resolved);
        }

        public void SetSelectedWorkspaceTab(string? tabKey)
        {
            var desired = string.IsNullOrWhiteSpace(tabKey) ? "TopWriteUp" : tabKey.Trim();

            _syncingWorkspaceTab = true;

            try
            {
                var targetTab = WorkspaceTabControl.Items
                    .OfType<TabItem>()
                    .FirstOrDefault(x =>
                        x.Visibility == Visibility.Visible &&
                        string.Equals(
                            x.Tag?.ToString(),
                            desired,
                            StringComparison.OrdinalIgnoreCase));

                if (targetTab is not null)
                    WorkspaceTabControl.SelectedItem = targetTab;
                else
                    WorkspaceTabControl.SelectedIndex = 0;

                var resolved = (WorkspaceTabControl.SelectedItem as TabItem)?.Tag?.ToString() ?? "TopWriteUp";
                ApplyWorkspaceTabVisualState(resolved, raiseChangedEvent: false);
            }
            finally
            {
                _syncingWorkspaceTab = false;
            }
        }

        private bool IsRangeExtenderDashboard => string.Equals(EquipmentDashboardKind, SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Rx,
            StringComparison.OrdinalIgnoreCase);

        private void ApplyDashboardFeatureVisibility()
        {
            var isRx = IsRangeExtenderDashboard;

            var isTower = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Tower,
                StringComparison.OrdinalIgnoreCase);

            if (RxOverviewTabItem is not null)
                RxOverviewTabItem.Visibility = isRx ? Visibility.Visible : Visibility.Collapsed;

            if (TowerOverviewTabItem is not null)
                TowerOverviewTabItem.Visibility = isTower ? Visibility.Visible : Visibility.Collapsed;

            if (SnmpTabItem is not null)
                SnmpTabItem.Visibility = isRx ? Visibility.Collapsed : Visibility.Visible;

            if (IncludePingStatsCheckBox is not null)
            {
                IncludePingStatsCheckBox.Visibility = isRx ? Visibility.Collapsed : Visibility.Visible;

                if (isRx)
                    IncludePingStatsCheckBox.IsChecked = false;
            }

            if (IncludeSnmpStatsCheckBox is not null)
            {
                IncludeSnmpStatsCheckBox.Visibility = isRx ? Visibility.Collapsed : Visibility.Visible;

                if (isRx)
                    IncludeSnmpStatsCheckBox.IsChecked = false;
            }

            if (SnmpCategoryOptionsPanel is not null && isRx)
                SnmpCategoryOptionsPanel.Visibility = Visibility.Collapsed;

            if (isRx)
            {
                var selectedKey = SelectedWorkspaceTabKey;

                if (string.Equals(selectedKey, "SNMPTool", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(selectedKey, "TopWriteUp", StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedWorkspaceTab("RxOverview");
                }
            }

            if (isTower)
            {
                var selectedKey = SelectedWorkspaceTabKey;

                if (string.Equals(selectedKey, "TopWriteUp", StringComparison.OrdinalIgnoreCase))
                    SetSelectedWorkspaceTab("TowerOverview");
            }
        }

        //Tower Stuff
        public void SetTowerSectors(IEnumerable<TowerSectorDto>? sectors)
        {
            _towerPingCards.Clear();

            if (TowerSectorCardsPanel is null)
                return;

            TowerSectorCardsPanel.Children.Clear();

            var sectorList = (sectors ?? Enumerable.Empty<TowerSectorDto>())
                .OrderBy(x => GetTowerSectorSortRank(x.Sector))
                .ThenBy(x => x.Sector)
                .ThenBy(x => x.TopSiteId)
                .ToList();

            if (sectorList.Count == 0)
            {
                TowerSectorCardsPanel.Children.Add(new TextBlock
                {
                    Text = "No tower sectors returned.",
                    Foreground = TryFindResource("TextSecondary") as Brush,
                    FontStyle = FontStyles.Italic
                });

                return;
            }

            foreach (var sector in sectorList)
            {
                var card = new TowerSectorPingCard
                {
                    Sector = string.IsNullOrWhiteSpace(sector.Sector) ? "Sector" : sector.Sector.Trim()
                };

                AddTowerEndpoint(card, "IP A", sector.IPa);
                AddTowerEndpoint(card, "IP B", sector.IPb);

                _towerPingCards.Add(card);
                TowerSectorCardsPanel.Children.Add(CreateTowerSectorCard(card));
            }
            Dispatcher.BeginInvoke(new Action(RefreshTowerSectorCardLayout));
        }

        private static int GetTowerSectorSortRank(string? sector)
        {
            var value = (sector ?? string.Empty).Trim().ToUpperInvariant();

            if (value == "AP1")
                return 1;

            if (value == "AP2")
                return 2;

            if (value == "AP3")
                return 3;

            if (value.StartsWith("AP") &&
                int.TryParse(value[2..], out var apNumber))
            {
                return 100 + apNumber;
            }

            return 1000;
        }

        private static void AddTowerEndpoint(TowerSectorPingCard card, string label, string? ip)
        {
            var cleanIp = (ip ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanIp) || cleanIp == "—")
                return;

            card.Endpoints.Add(new TowerPingEndpoint
            {
                ParentSector = card,
                Label = label,
                IpAddress = cleanIp
            });
        }

        private FrameworkElement CreateTowerSectorCard(TowerSectorPingCard card)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SurfaceBg") as Brush,
                Padding = new Thickness(12),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            card.CardBorder = border;

            var root = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var title = new TextBlock
            {
                Text = $"Sector {card.Sector}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var controls = new Grid();
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pingCountBox = new TextBox
            {
                Style = (Style)FindResource("ModernWatermarkTextBox"),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = "Ping Count",
                Text = string.Empty
            };

            card.PingCountTextBox = pingCountBox;

            var pingSectorButton = new Button
            {
                Content = "Ping Sector",
                Style = (Style)FindResource("PrimaryButtonStyle"),
                Height = 28,
                MinWidth = 92,
                Padding = new Thickness(12, 0, 12, 0),
                Tag = card
            };
            pingSectorButton.Click += PingTowerSectorButton_Click;

            var stopButton = new Button
            {
                Content = "Stop",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 28,
                MinWidth = 70,
                Padding = new Thickness(12, 0, 12, 0),
                Tag = card
            };
            stopButton.Click += StopTowerSectorButton_Click;

            var clearButton = new Button
            {
                Content = "Clear",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 28,
                MinWidth = 70,
                Padding = new Thickness(12, 0, 12, 0),
                Tag = card
            };
            clearButton.Click += ClearTowerSectorButton_Click;

            Grid.SetColumn(pingCountBox, 0);
            Grid.SetColumn(pingSectorButton, 2);
            Grid.SetColumn(stopButton, 4);
            Grid.SetColumn(clearButton, 6);

            controls.Children.Add(pingCountBox);
            controls.Children.Add(pingSectorButton);
            controls.Children.Add(stopButton);
            controls.Children.Add(clearButton);

            Grid.SetRow(controls, 2);
            root.Children.Add(controls);

            var endpointGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var endpointCount = Math.Max(1, card.Endpoints.Count);

            for (var i = 0; i < endpointCount; i++)
            {
                endpointGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });
            }

            for (var i = 0; i < card.Endpoints.Count; i++)
            {
                var endpointCard = CreateTowerPingEndpointCard(card, card.Endpoints[i]);

                if (endpointCard is FrameworkElement fe)
                {
                    fe.Margin = i == card.Endpoints.Count - 1
                        ? new Thickness(0)
                        : new Thickness(0, 0, 0, 8);
                }

                Grid.SetRow(endpointCard, i);
                endpointGrid.Children.Add(endpointCard);
            }

            Grid.SetRow(endpointGrid, 4);
            root.Children.Add(endpointGrid);

            border.Child = root;
            return border;
        }

        private async void TestAllTowerSectorsButton_Click(object sender, RoutedEventArgs e)
        {
            StopTowerPings();

            foreach (var sector in _towerPingCards)
                await TestTowerSectorAsync(sector);
        }

        private FrameworkElement CreateTowerPingEndpointCard(TowerSectorPingCard sector, TowerPingEndpoint endpoint)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Background = TryFindResource("CardBg") as Brush,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var root = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            top.Children.Add(new TextBlock
            {
                Text = endpoint.Label,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            });

            var pingButton = new Button
            {
                Content = "Ping",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 24,
                MinWidth = 58,
                Padding = new Thickness(8, 0, 8, 0),
                Tag = endpoint
            };

            pingButton.Click += PingTowerEndpointButton_Click;

            Grid.SetColumn(pingButton, 1);
            top.Children.Add(pingButton);

            Grid.SetRow(top, 0);
            root.Children.Add(top);

            var ipBox = new TextBox
            {
                Text = endpoint.IpAddress,
                Style = (Style)FindResource("ModernTextBox"),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };

            endpoint.IpTextBox = ipBox;
            endpoint.DefaultIpBorderBrush = ipBox.BorderBrush;
            endpoint.DefaultIpBackground = ipBox.Background;
            endpoint.DefaultIpForeground = ipBox.Foreground;

            Grid.SetRow(ipBox, 2);
            root.Children.Add(ipBox);

            var resultBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(8),
                Text = string.Empty,
                Height = double.NaN,
                MinHeight = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            endpoint.ResultTextBox = resultBox;

            Grid.SetRow(resultBox, 4);
            root.Children.Add(resultBox);

            var summary = new TextBlock
            {
                Text = "Ready.",
                Foreground = TryFindResource("TextSecondary") as Brush,
                FontSize = 11
            };

            endpoint.SummaryTextBlock = summary;

            Grid.SetRow(summary, 6);
            root.Children.Add(summary);

            border.Child = root;
            return border;
        }

        private bool TryGetTowerSectorPingCount(TowerSectorPingCard sector, out int pingCount, out bool continuous)
        {
            pingCount = 0;
            continuous = false;

            var raw = (sector.PingCountTextBox?.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                continuous = true;
                return true;
            }

            if (!int.TryParse(raw, out pingCount) || pingCount < 1 || pingCount > 99999)
            {
                MessageBox.Show(
                    "Enter a whole number between 1 and 99,999, or leave it blank for continuous ping.",
                    "Tower Ping Count",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                sector.PingCountTextBox?.Focus();
                return false;
            }

            return true;
        }


        private void ResetTowerIpStatus(TowerPingEndpoint endpoint)
        {
            if (endpoint.IpTextBox is null)
                return;

            endpoint.IpTextBox.BorderBrush = endpoint.DefaultIpBorderBrush;
            endpoint.IpTextBox.Background = endpoint.DefaultIpBackground;
            endpoint.IpTextBox.Foreground = endpoint.DefaultIpForeground;
        }

        private void ApplyTowerIpStatus(TowerPingEndpoint endpoint, bool success)
        {
            if (endpoint.IpTextBox is null)
                return;

            var border = success
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));

            var background = success
                ? new SolidColorBrush(Color.FromRgb(232, 245, 233))
                : new SolidColorBrush(Color.FromRgb(253, 236, 234));

            endpoint.IpTextBox.BorderBrush = border;
            endpoint.IpTextBox.Background = background;
        }

        private void TowerSectorCardsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshTowerSectorCardLayout();
        }

        private void RefreshTowerSectorCardLayout()
        {
            if (TowerSectorCardsScrollViewer is null || _towerPingCards.Count == 0)
                return;

            var viewportWidth = TowerSectorCardsScrollViewer.ViewportWidth;
            if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
                viewportWidth = TowerSectorCardsScrollViewer.ActualWidth;

            var availableHeight = TowerSectorCardsScrollViewer.ActualHeight;

            if (double.IsNaN(viewportWidth) || viewportWidth <= 100 ||
                double.IsNaN(availableHeight) || availableHeight <= 100)
            {
                return;
            }

            var showHorizontalScroll = _towerPingCards.Count > 3;
            TowerSectorCardsScrollViewer.HorizontalScrollBarVisibility =
                showHorizontalScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

            var visibleColumns = Math.Min(3, _towerPingCards.Count);

            const double cardGap = 10;
            var totalGapWidth = cardGap * Math.Max(0, visibleColumns - 1);

            var cardWidth = (viewportWidth - totalGapWidth) / visibleColumns;
            cardWidth = Math.Max(280, cardWidth);

            var horizontalBarAllowance = showHorizontalScroll
                ? SystemParameters.HorizontalScrollBarHeight + 6
                : 0;

            var cardHeight = Math.Max(320, availableHeight - horizontalBarAllowance - 6);

            for (var i = 0; i < _towerPingCards.Count; i++)
            {
                var card = _towerPingCards[i];

                if (card.CardBorder is null)
                    continue;

                card.CardBorder.Width = cardWidth;
                card.CardBorder.Height = cardHeight;
                card.CardBorder.Margin = (i == _towerPingCards.Count - 1)
                    ? new Thickness(0)
                    : new Thickness(0, 0, 10, 0);
            }
        }

        private async void PingTowerEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerPingEndpoint endpoint)
                return;

            await RunSingleTowerEndpointPingAsync(endpoint);
        }

        private async Task RunSingleTowerEndpointPingAsync(TowerPingEndpoint endpoint)
        {
            var sector = endpoint.ParentSector;

            if (sector is null)
                return;

            if (!TryGetTowerSectorPingCount(sector, out var pingCount, out var continuous))
                return;

            StopTowerSectorPings(sector);

            sector.PingCts = new CancellationTokenSource();
            var token = sector.PingCts.Token;
            sector.IsRunning = true;

            try
            {
                await PingTowerEndpointAsync(endpoint, pingCount, continuous, token);
            }
            catch (OperationCanceledException)
            {
                // expected when stopped
            }
            finally
            {
                sector.IsRunning = false;
            }
        }

        private async Task PingTowerEndpointAsync(TowerPingEndpoint endpoint, int pingCount, bool continuous, CancellationToken token)
        {
            if (endpoint.IsRunning)
                return;

            endpoint.IsRunning = true;

            try
            {
                var ip = (endpoint.IpAddress ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(ip))
                    return;

                ResetTowerIpStatus(endpoint);

                if (endpoint.ResultTextBox is not null)
                    endpoint.ResultTextBox.Text = string.Empty;

                if (endpoint.SummaryTextBlock is not null)
                    endpoint.SummaryTextBlock.Text = "Testing...";

                var sent = 0;
                var received = 0;
                var outputLines = new List<string>();

                using var ping = new Ping();

                while (!token.IsCancellationRequested && (continuous || sent < pingCount))
                {
                    sent++;

                    try
                    {
                        var reply = await ping.SendPingAsync(ip, 1000);

                        if (reply.Status == IPStatus.Success)
                        {
                            received++;
                            outputLines.Add($"Reply from {ip}: Time={reply.RoundtripTime}ms");
                            ApplyTowerIpStatus(endpoint, true);
                        }
                        else
                        {
                            outputLines.Add($"{ip}: {reply.Status}");
                            ApplyTowerIpStatus(endpoint, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        outputLines.Add($"{ip}: {ex.Message}");
                        ApplyTowerIpStatus(endpoint, false);
                    }

                    if (outputLines.Count > 150)
                        outputLines.RemoveRange(0, outputLines.Count - 150);

                    if (endpoint.ResultTextBox is not null)
                    {
                        endpoint.ResultTextBox.Text = string.Join(Environment.NewLine, outputLines);
                        endpoint.ResultTextBox.ScrollToEnd();
                    }

                    var lost = sent - received;
                    var lossPercent = sent == 0
                        ? 0
                        : (int)Math.Round((lost / (double)sent) * 100);

                    if (endpoint.SummaryTextBlock is not null)
                    {
                        endpoint.SummaryTextBlock.Text = continuous
                            ? $"Sent = {sent}, Lost = {lost} ({lossPercent}% loss) • Running..."
                            : $"Sent = {sent}, Lost = {lost} ({lossPercent}% loss)";
                    }

                    var delayMs = continuous ? 1000 : 150;
                    await Task.Delay(delayMs, token);
                }
            }
            finally
            {
                endpoint.IsRunning = false;
            }
        }

        private async void PingTowerSectorButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerSectorPingCard sector)
                return;

            await RunTowerSectorPingAsync(sector);
        }

        private async Task RunTowerSectorPingAsync(TowerSectorPingCard sector)
        {
            if (sector.IsRunning)
                return;

            if (!TryGetTowerSectorPingCount(sector, out var pingCount, out var continuous))
                return;

            StopTowerSectorPings(sector);

            sector.PingCts = new CancellationTokenSource();
            var token = sector.PingCts.Token;
            sector.IsRunning = true;

            try
            {
                var tasks = sector.Endpoints
                    .Where(x => !x.IsRunning)
                    .Select(x => PingTowerEndpointAsync(x, pingCount, continuous, token))
                    .ToList();

                if (tasks.Count > 0)
                    await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // expected when stopped
            }
            finally
            {
                sector.IsRunning = false;
            }
        }

        private void StopTowerSectorButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerSectorPingCard sector)
                return;

            StopTowerSectorPings(sector);
        }

        private void ClearTowerSectorButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerSectorPingCard sector)
                return;

            StopTowerSectorPings(sector);

            foreach (var endpoint in sector.Endpoints)
            {
                ResetTowerIpStatus(endpoint);

                if (endpoint.ResultTextBox is not null)
                    endpoint.ResultTextBox.Text = string.Empty;

                if (endpoint.SummaryTextBlock is not null)
                    endpoint.SummaryTextBlock.Text = "Ready.";
            }
        }        

        private void StopTowerSectorPings(TowerSectorPingCard sector)
        {
            try
            {
                sector.PingCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            sector.PingCts?.Dispose();
            sector.PingCts = null;
            sector.IsRunning = false;
        }

        private async Task TestTowerSectorAsync(TowerSectorPingCard sector)
        {
            StopTowerSectorPings(sector);

            foreach (var endpoint in sector.Endpoints)
                await TestTowerEndpointAsync(endpoint);
        }

        private async Task TestTowerEndpointAsync(TowerPingEndpoint endpoint)
        {
            var ip = (endpoint.IpAddress ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            ResetTowerIpStatus(endpoint);

            var successAfterWarmup = false;

            using var ping = new Ping();

            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(ip, 1000);

                    if (reply.Status == IPStatus.Success && i > 0)
                        successAfterWarmup = true;
                }
                catch
                {
                    // Treat failed ping attempt as no response.
                }
            }

            ApplyTowerIpStatus(endpoint, successAfterWarmup);
        }

        public void StopTowerPings()
        {
            foreach (var sector in _towerPingCards)
                StopTowerSectorPings(sector);
        }

        private void RefreshTowerHeaderDisplay()
        {
            if (TowerHeaderTextBlock is null)
                return;

            var topName = GetTowerSummaryValue("Top Name");
            var description = GetTowerSummaryValue("Description");

            var cleanedDescription = CleanTowerHeaderDescription(description);

            if (!string.IsNullOrWhiteSpace(cleanedDescription) &&
                !string.IsNullOrWhiteSpace(topName))
            {
                TowerHeaderTextBlock.Text = $"Tower {cleanedDescription} ({topName})";
            }
            else if (!string.IsNullOrWhiteSpace(topName))
            {
                TowerHeaderTextBlock.Text = $"Tower {topName}";
            }
            else
            {
                TowerHeaderTextBlock.Text = "Tower";
            }
        }

        private string GetTowerSummaryValue(string label)
        {
            var lines = (_towerSummaryText ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    continue;

                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                return line[(idx + 1)..].Trim();
            }

            return string.Empty;
        }

        private static string CleanTowerHeaderDescription(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (text.EndsWith(" SUB", StringComparison.OrdinalIgnoreCase))
                text = text[..^4].Trim();

            return text;
        }


        //Range Extnder Stuff
        private void OpenRangeExtenderLinkButton_Click(object sender, RoutedEventArgs e)
        {
            var url = (RangeExtenderLinkUrl ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(
                    "Range Extender link URL has not been configured yet.",
                    "Range Extender",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open Range Extender link:{Environment.NewLine}{ex.Message}",
                    "Range Extender",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SearchRxIpButton_Click(object sender, RoutedEventArgs e)
        {
            var ip = (RxIpLookupTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip))
            {
                RxIpLookupStatusTextBlock.Text = "Enter an IP address first.";
                RxAssociatedSiteTextBlock.Text = string.Empty;
                OpenAssociatedSiteButton.IsEnabled = false;
                _rxAssociatedSiteId = string.Empty;
                return;
            }

            if (!IpRegex.IsMatch(ip))
            {
                RxIpLookupStatusTextBlock.Text = "Enter a valid IPv4 address.";
                RxAssociatedSiteTextBlock.Text = string.Empty;
                OpenAssociatedSiteButton.IsEnabled = false;
                _rxAssociatedSiteId = string.Empty;
                return;
            }

            RxIpLookupStatusTextBlock.Text = "Searching...";
            RxAssociatedSiteTextBlock.Text = string.Empty;
            OpenAssociatedSiteButton.IsEnabled = false;
            _rxAssociatedSiteId = string.Empty;

            RxIpLookupRequested?.Invoke(this, ip);
        }

        private void OpenAssociatedSiteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_rxAssociatedSiteId))
                return;

            OpenAssociatedSiteRequested?.Invoke(this, _rxAssociatedSiteId);
        }

        public void ShowRxIpLookupResult(string? siteId, string? message = null)
        {
            var cleanSite = (siteId ?? string.Empty).Trim();

            _rxAssociatedSiteId = cleanSite;

            if (string.IsNullOrWhiteSpace(cleanSite))
            {
                RxIpLookupStatusTextBlock.Text = string.IsNullOrWhiteSpace(message)
                    ? "No associated site found for that IP."
                    : message.Trim();

                RxAssociatedSiteTextBlock.Text = string.Empty;
                OpenAssociatedSiteButton.IsEnabled = false;
                return;
            }

            RxIpLookupStatusTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Associated site found."
                : message.Trim();

            RxAssociatedSiteTextBlock.Text = cleanSite;
            OpenAssociatedSiteButton.IsEnabled = true;
        }

        private void RefreshRangeExtenderPanel()
        {
            if (RxRangeExtenderSnTextBox is null)
                return;

            RxRangeExtenderSnTextBox.Text = DashForDisplay(GetTopInfoValue("Range Extender SN"));
            RxMacAddressTextBox.Text = DashForDisplay(GetTopInfoValue("MAC Address"));
            RxPolePointTextBox.Text = DashForDisplay(GetTopInfoValue("Pole Point"));
            RxTransformerGlnTextBox.Text = DashForDisplay(GetTopInfoValue("Transformer GLN"));
        }

        private static string DashForDisplay(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        }

        private async void CopyRxReferenceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var value = button.Tag?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return;

            var copied = await TryCopyToClipboardAsync(value);

            if (!copied)
            {
                button.ToolTip = "Could not copy. Try again.";
                return;
            }

            var icon = FindVisualChildren<TextBlock>(button).FirstOrDefault();

            if (icon is null)
                return;

            icon.Text = CheckGlyph;
            button.ToolTip = "Copied!";

            await Task.Delay(TimeSpan.FromSeconds(3));

            icon.Text = CopyGlyph;
        }


        //History
        public void SetHistoryRows(IEnumerable<SiteDashboardHistoryRowViewModel> rows)
        {
            HistoryDataGrid.ItemsSource = rows?.ToList() ?? new List<SiteDashboardHistoryRowViewModel>();
            HistoryDataGrid.SelectedItem = null;
            NarrativeTextBlock.Text = string.Empty;
        }

        private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HistoryDataGrid.SelectedItem is SiteDashboardHistoryRowViewModel row)
                NarrativeTextBlock.Text = CleanNarrativeText(row.NarrativeText);
            else
                NarrativeTextBlock.Text = string.Empty;
        }
                
        //Removes \n\n in narrative texts
        private static string CleanNarrativeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            while (normalized.Contains("\n\n\n"))
                normalized = normalized.Replace("\n\n\n", "\n\n");

            return normalized.Trim();
        }

        //End of History



        //SNMP
        private void PollAllSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in _snmpCategoryGroups.Where(x => x.Rows.Count > 0))
            {
                var oids = group.Rows.Select(x => x.Oid).ToList();
                RunSnmpCategoryRequested?.Invoke(this, new SnmpRunCategoryRequestedEventArgs(group.Category, oids));
            }
        }

        public void ResetSnmp()
        {
            _snmpPrimaryIp = null;
            _snmpLanIp = null;
            _snmpSecondaryIp = null;
            _snmpCategoryGroups = new List<SnmpCategoryGroupViewModel>();

            _syncingSnmpProfileCombo = true;
            SnmpProfileComboBox.ItemsSource = null;
            SnmpProfileComboBox.SelectedItem = null;
            _syncingSnmpProfileCombo = false;

            _syncingWritableOidCombo = true;
            SnmpWritableOidComboBox.ItemsSource = null;
            SnmpWritableOidComboBox.SelectedItem = null;
            _syncingWritableOidCombo = false;

            SnmpCategoryItemsControl.ItemsSource = null;
            SnmpSupportInlineTextBlock.Text = "No site loaded.";

            _syncingSnmpTargetCombo = true;
            SnmpTargetComboBox.SelectedIndex = -1;
            _syncingSnmpTargetCombo = false;
            SnmpTargetTextBox.Text = string.Empty;

            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueTextBox.Visibility = Visibility.Visible;
            SnmpSetValueTextBox.IsEnabled = false;

            SnmpSetValueComboBox.ItemsSource = null;
            SnmpSetValueComboBox.SelectedItem = null;
            SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
            SnmpSetValueComboBox.IsEnabled = false;

            SetSelectedSnmpButton.IsEnabled = false;
            SnmpDecoderValuesTextBox.Text = string.Empty;
        }

        public void SetSnmpContext(bool supported, string supportMessage, string deviceFamily, string profileName, string? primaryIp, string? lanIp,
            string? secondaryIp, string? targetIp)
        {
            _snmpPrimaryIp = primaryIp;
            _snmpLanIp = lanIp;
            _snmpSecondaryIp = secondaryIp;

            SnmpSupportInlineTextBlock.Text = string.IsNullOrWhiteSpace(supportMessage)
                ? "—"
                : supportMessage;

            var targets = new List<SnmpTargetChoice>();

            AddSnmpTargetChoice(targets, "Primary", "Primary IP", primaryIp);
            AddSnmpTargetChoice(targets, "LAN", "LAN IP", lanIp);
            AddSnmpTargetChoice(targets, "Secondary", "Secondary IP", secondaryIp);

            ApplySnmpTargetChoices(targets, targetIp);
        }

        public void SetSnmpTargetOptions(IEnumerable<(string Key, string Label, string IpAddress)> targets, string? targetIp)
        {
            var list = new List<SnmpTargetChoice>();

            foreach (var target in targets)
            {
                AddSnmpTargetChoice(
                    list,
                    target.Key,
                    target.Label,
                    target.IpAddress);
            }

            ApplySnmpTargetChoices(list, targetIp);
        }

        private void ApplySnmpTargetChoices(List<SnmpTargetChoice> targets, string? targetIp)
        {
            var cleanTargetIp = (targetIp ?? string.Empty).Trim();

            _syncingSnmpTargetCombo = true;

            SnmpTargetComboBox.ItemsSource = null;
            SnmpTargetComboBox.DisplayMemberPath = nameof(SnmpTargetChoice.DisplayLabel);
            SnmpTargetComboBox.SelectedValuePath = nameof(SnmpTargetChoice.Key);
            SnmpTargetComboBox.ItemsSource = targets;

            SnmpTargetChoice? selected = null;

            if (!string.IsNullOrWhiteSpace(cleanTargetIp))
            {
                selected = targets.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.IpAddress) &&
                    string.Equals(x.IpAddress, cleanTargetIp, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                selected = targets.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.IpAddress));
            }

            SnmpTargetComboBox.SelectedItem = selected;

            _syncingSnmpTargetCombo = false;

            SnmpTargetTextBox.Text = !string.IsNullOrWhiteSpace(cleanTargetIp)
                ? cleanTargetIp
                : selected?.IpAddress ?? string.Empty;
        }

        public void SetSnmpProfiles(IEnumerable<SnmpProfileListItemDto> profiles, ulong? selectedProfileId)
        {
            _syncingSnmpProfileCombo = true;

            var list = (profiles ?? Enumerable.Empty<SnmpProfileListItemDto>())
                .Select(x => new SnmpProfileChoice
                {
                    Id = x.Id,
                    DisplayLabel = x.Name
                })
                .ToList();

            SnmpProfileComboBox.ItemsSource = list;

            if (selectedProfileId.HasValue)
                SnmpProfileComboBox.SelectedValue = selectedProfileId.Value;
            else
                SnmpProfileComboBox.SelectedItem = null;

            _syncingSnmpProfileCombo = false;
        }

        public void SetSnmpOids(IEnumerable<SnmpOidConfigDto> oids, IDictionary<ulong, string>? resultMap = null)
        {
            var list = oids?.ToList() ?? new List<SnmpOidConfigDto>();

            _snmpCategoryGroups = BuildSnmpCategoryGroups(list, resultMap);
            SnmpCategoryItemsControl.ItemsSource = _snmpCategoryGroups;

            _syncingWritableOidCombo = true;

            var writable = list
                .Where(x => x.IsWritable)
                .OrderBy(x => x.Label)
                .Select(x => new SnmpWritableOidChoice
                {
                    DisplayLabel = x.Label,
                    Oid = x
                })
                .ToList();

            SnmpWritableOidComboBox.ItemsSource = writable;
            SnmpWritableOidComboBox.SelectedItem = null;

            _syncingWritableOidCombo = false;

            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueTextBox.Visibility = Visibility.Visible;
            SnmpSetValueTextBox.IsEnabled = false;

            SnmpSetValueComboBox.ItemsSource = null;
            SnmpSetValueComboBox.SelectedItem = null;
            SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
            SnmpSetValueComboBox.IsEnabled = false;

            SetSelectedSnmpButton.IsEnabled = false;
            SnmpDecoderValuesTextBox.Text = string.Empty;
        }

        private void SnmpTargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSnmpTargetCombo)
                return;

            if (SnmpTargetComboBox.SelectedItem is SnmpTargetChoice choice)
                SnmpTargetTextBox.Text = choice.IpAddress ?? string.Empty;

            SnmpTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        public ulong? GetSelectedSnmpProfileId()
        {
            if (SnmpProfileComboBox.SelectedValue is ulong id)
                return id;

            if (SnmpProfileComboBox.SelectedItem is SnmpProfileChoice choice)
                return choice.Id;

            return null;
        }

        public SnmpOidConfigDto? GetSelectedWritableSnmpOid()
        {
            return (SnmpWritableOidComboBox.SelectedItem as SnmpWritableOidChoice)?.Oid;
        }

        public string GetSnmpTargetIp()
        {
            return (SnmpTargetTextBox.Text ?? string.Empty).Trim();
        }

        public string GetSnmpSetValue()
        {
            if (SnmpSetValueComboBox.Visibility == Visibility.Visible)
            {
                if (SnmpSetValueComboBox.SelectedValue is string raw)
                    return raw.Trim();

                if (SnmpSetValueComboBox.SelectedItem is SnmpSetValueChoice choice)
                    return choice.RawValue.Trim();

                return string.Empty;
            }

            return (SnmpSetValueTextBox.Text ?? string.Empty).Trim();
        }

        public void SetSnmpOidResult(ulong oidId, string resultText)
        {
            foreach (var row in _snmpCategoryGroups.SelectMany(x => x.Rows))
            {
                if (row.Id == oidId)
                {
                    row.ResultText = resultText;
                    return;
                }
            }
        }

        private List<SnmpCategoryGroupViewModel> BuildSnmpCategoryGroups(IReadOnlyCollection<SnmpOidConfigDto> oids, IDictionary<ulong, string>? resultMap)
        {
            var categoryOrder = new[] { "Admin", "Config", "Stats" };
            var groups = new List<SnmpCategoryGroupViewModel>();

            foreach (var category in categoryOrder)
            {
                var rows = oids
                    .Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Label)
                    .Select(x => new SnmpOidRowViewModel
                    {
                        Oid = x,
                        ResultText = resultMap is not null && resultMap.TryGetValue(x.Id, out var result)
                            ? result
                            : string.Empty
                    })
                    .ToList();

                groups.Add(new SnmpCategoryGroupViewModel
                {
                    Category = category,
                    Rows = new ObservableCollection<SnmpOidRowViewModel>(rows)
                });
            }

            return groups;
        }

        private void RefreshSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshSnmpRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SetSelectedSnmpButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedSnmpRequested?.Invoke(this, EventArgs.Empty);
        }        

        private void SnmpTargetTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SnmpTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SnmpProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSnmpProfileCombo)
                return;

            SelectedSnmpProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SnmpWritableOidComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingWritableOidCombo)
                return;

            var choice = SnmpWritableOidComboBox.SelectedItem as SnmpWritableOidChoice;
            UpdateWritableSnmpUi(choice?.Oid);
        }

        private void RunSnmpOidButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is SnmpOidRowViewModel row)
                RunSnmpOidRequested?.Invoke(this, new SnmpRunOidRequestedEventArgs(row.Oid));
        }

        private static void AddSnmpTargetChoice(ICollection<SnmpTargetChoice> items, string key, string label, string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return;

            var trimmed = ip.Trim();

            items.Add(new SnmpTargetChoice
            {
                Key = key,
                IpAddress = trimmed,
                DisplayLabel = label
            });
        }

        private void UpdateWritableSnmpUi(SnmpOidConfigDto? oid)
        {
            SnmpSetValueTextBox.Text = string.Empty;
            SnmpSetValueComboBox.ItemsSource = null;
            SnmpSetValueComboBox.SelectedItem = null;

            if (oid is null)
            {
                SnmpSetValueTextBox.Visibility = Visibility.Visible;
                SnmpSetValueTextBox.IsEnabled = false;
                SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
                SnmpSetValueComboBox.IsEnabled = false;
                SetSelectedSnmpButton.IsEnabled = false;
                SnmpDecoderValuesTextBox.Text = string.Empty;
                return;
            }

            if (!oid.IsWritable)
            {
                SnmpSetValueTextBox.Visibility = Visibility.Visible;
                SnmpSetValueTextBox.IsEnabled = false;
                SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
                SnmpSetValueComboBox.IsEnabled = false;
                SetSelectedSnmpButton.IsEnabled = false;
                SnmpDecoderValuesTextBox.Text = "Selected OID is read-only.";
                return;
            }

            SetSelectedSnmpButton.IsEnabled = true;

            if (oid.DecodeValues is { Count: > 0 })
            {
                var decodeChoices = oid.DecodeValues
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.RawValue)
                    .Select(x => new SnmpSetValueChoice
                    {
                        RawValue = x.RawValue,
                        DisplayLabel = $"{x.RawValue} = {x.DisplayText}"
                    })
                    .ToList();

                SnmpSetValueTextBox.Visibility = Visibility.Collapsed;
                SnmpSetValueTextBox.IsEnabled = false;

                SnmpSetValueComboBox.ItemsSource = decodeChoices;
                SnmpSetValueComboBox.Visibility = Visibility.Visible;
                SnmpSetValueComboBox.IsEnabled = true;
                SnmpSetValueComboBox.SelectedIndex = 0;

                SnmpDecoderValuesTextBox.Text =
                    "Decoder Values:" + Environment.NewLine +
                    string.Join(Environment.NewLine, decodeChoices.Select(x => x.DisplayLabel));
            }
            else
            {
                SnmpSetValueComboBox.Visibility = Visibility.Collapsed;
                SnmpSetValueComboBox.IsEnabled = false;

                SnmpSetValueTextBox.Visibility = Visibility.Visible;
                SnmpSetValueTextBox.IsEnabled = true;

                SnmpDecoderValuesTextBox.Text = "No decoder values configured for this OID. Enter the raw value manually.";
            }
        }

        public void ShowSnmpSetResult(SnmpSetResultDto? result)
        {
            if (result is null)
            {
                SnmpDecoderValuesTextBox.Text = "No SNMP set result.";
                return;
            }

            SnmpDecoderValuesTextBox.Text = result.Success
                ? $"Set succeeded for {result.Label}:{Environment.NewLine}{result.DisplayValue}"
                : $"Set failed for {result.Label}:{Environment.NewLine}{result.ErrorMessage}";
        }

        private static bool IsUsefulSnmpResultText(string? rawValue)
        {
            var value = (rawValue ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Equals("—", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Ready.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Running...", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Polling...", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("No data", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("No value", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Not polled", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Not polled.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (value.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("ERROR ", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("SNMP not supported", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("No active SNMP profile", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string NormalizeSnmpResultForWriteUp(string? rawValue)
        {
            var value = (rawValue ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var parts = value
                .Split(new[] { "\r\n", "\n", "\r", "\t" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var normalized = parts.Count == 0
                ? value
                : string.Join(" | ", parts);

            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ");

            return normalized.Trim();
        }

        //SNMP Helpers
        private sealed class SnmpProfileChoice
        {
            public ulong Id { get; set; }
            public string DisplayLabel { get; set; } = "";

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpWritableOidChoice
        {
            public string DisplayLabel { get; set; } = "";
            public SnmpOidConfigDto Oid { get; set; } = new();

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpTargetChoice
        {
            public string Key { get; set; } = "";
            public string DisplayLabel { get; set; } = "";
            public string IpAddress { get; set; } = "";

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpSetValueChoice
        {
            public string RawValue { get; set; } = "";
            public string DisplayLabel { get; set; } = "";

            public override string ToString() => DisplayLabel;
        }

        private sealed class SnmpOidRowViewModel : INotifyPropertyChanged
        {
            private string _resultText = string.Empty;

            public SnmpOidConfigDto Oid { get; set; } = new();

            public ulong Id => Oid.Id;
            public string Label => Oid.Label;

            public string ResultText
            {
                get => _resultText;
                set
                {
                    if (_resultText == value)
                        return;

                    _resultText = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private sealed class SnmpCategoryGroupViewModel
        {
            public string Category { get; set; } = "";
            public ObservableCollection<SnmpOidRowViewModel> Rows { get; set; } = new();

            public string EmptyMessage => Rows.Count == 0 ? "No OIDs configured." : string.Empty;
            public Visibility EmptyMessageVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        //End of SNMP



        //Portal 
        public async Task EnsurePortalReadyAsync()
        {
            if (_portalInitialized)
                return;

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartGridSuite",
                "WebView2",
                "IgsdPortal");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await PortalWebView.EnsureCoreWebView2Async(env);

            PortalWebView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;

                if (!string.IsNullOrWhiteSpace(e.Uri))
                    PortalWebView.CoreWebView2.Navigate(e.Uri);
            };

            _portalInitialized = true;
        }

        public async Task NavigatePortalAsync(bool forceReload = false)
        {
            if (string.IsNullOrWhiteSpace(_portalUrl))
                return;

            await EnsurePortalReadyAsync();

            var requestedUrl = _portalUrl.Trim();

            if (forceReload || !string.Equals(_lastPortalRequestedUrl, requestedUrl, StringComparison.OrdinalIgnoreCase))
            {
                PortalWebView.CoreWebView2.Navigate(requestedUrl);
                _lastPortalRequestedUrl = requestedUrl;
            }
        }

        private async void ReloadPortalButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigatePortalAsync(forceReload: true);
        }

        private void OpenPortalInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_portalUrl))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = _portalUrl,
                UseShellExecute = true
            });
        }

        //End of Portal


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

            _equipmentText = string.Empty;
            RefreshEquipmentCards();

            // Workspace tab
            SetSelectedWorkspaceTab("TopWriteUp");

            // SNMP
            ResetSnmp();
        }

        private void WorkspaceTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingWorkspaceTab)
                return;

            if (WorkspaceTabControl.SelectedItem is not TabItem tab)
                return;

            ApplyWorkspaceTabVisualState(tab.Tag as string, raiseChangedEvent: true);
        }

        private void WriteUpTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            WriteUpTextChanged?.Invoke(this, WriteUpTextBox.Text);
        }

        

        //Equipment Tab

        private sealed class SerializedDeviceInfo
        {
            public string Label { get; set; } = string.Empty;
            public string OldSerial { get; set; } = string.Empty;
            public string ReplacementKey { get; set; } = string.Empty;
            public bool UsesCommunicationDeviceTypePicker { get; set; }
        }

        private int _serializedDeviceSectionCount;
        private readonly HashSet<string> _activeReplacementEntryKeys = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxReplacementEntries = 15;

        public void RefreshEquipmentDisplay()
        {
            RefreshEquipmentCards();
        }

        private void RefreshEquipmentCards()
        {
            if (SerializedDevicesPanel is null || AccessSecuritySectionPanel is null)
                return;

            SerializedDevicesPanel.Children.Clear();
            AccessSecuritySectionPanel.Children.Clear();
            _serializedDeviceSectionCount = 0;

            var isIgsd = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Igsd,
                StringComparison.OrdinalIgnoreCase);

            var isAmsMr = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.AmsMr,
                StringComparison.OrdinalIgnoreCase);

            var isDacs = string.Equals(
                EquipmentDashboardKind,
                SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Dacs,
                StringComparison.OrdinalIgnoreCase);

            var isRx = IsRangeExtenderDashboard;

            if (AccessSecurityCard is not null)
                AccessSecurityCard.Visibility = (isRx || isDacs) ? Visibility.Collapsed : Visibility.Visible;

            if (isRx)
            {
                AddSerializedDeviceSection(
                    title: "Range Extender",
                    model: null,
                    serial: GetEquipmentValue("Range Extender SN", "Meter Number"),
                    swapLabel: "Range Extender");

                return;
            }

            if (isDacs)
            {
                AddSerializedDeviceSection(
                    title: "Primary Communications",
                    model: null,
                    serial: GetEquipmentValue("Primary SN", "Primary Communications SN", "Radio SN"),
                    swapLabel: "Primary Communications",
                    usesCommunicationDeviceTypePicker: true);

                AddSerializedDeviceSection(
                    title: "Antenna",
                    model: null,
                    serial: GetEquipmentValue("Antenna SN"),
                    swapLabel: "Antenna");

                return;
            }

            AddSerializedDeviceSection(
                title: "Enclosure",
                model: GetEquipmentValue("Enclosure Model"),
                serial: GetEquipmentValue("Enclosure SN"),
                swapLabel: "Enclosure",
                showModelBesideSerial: true);

            AddSerializedDeviceSection(
                title: "Primary Communications",
                model: null,
                serial: GetEquipmentValue("Primary SN"),
                swapLabel: "Primary Communications",
                usesCommunicationDeviceTypePicker: true);

            AddSerializedDeviceSection(
                title: "Secondary Communications",
                model: null,
                serial: GetEquipmentValue("Secondary SN"),
                swapLabel: "Secondary Communications",
                usesCommunicationDeviceTypePicker: true);

            AddSerializedDeviceSection(
                title: "Antenna",
                model: null,
                serial: GetEquipmentValue("Antenna SN"),
                swapLabel: "Antenna");

            if (isIgsd)
            {
                AddSerializedDeviceSection(
                    title: "Cyberlock",
                    model: null,
                    serial: GetEquipmentValue("Cyberlock SN"),
                    swapLabel: "Cyberlock");
            }

            var hasSensitiveRows = false;

            if (isIgsd)
            {
                hasSensitiveRows |= AddSensitiveEquipmentRow(
                    "Tunnel PSK",
                    GetEquipmentValue("Tunnel PSK"));
            }

            if (isAmsMr)
            {
                hasSensitiveRows |= AddSensitiveEquipmentRow(
                    "Secondary WiFi SSID",
                    GetEquipmentValue("Secondary WiFi SSID", "Secondary SSID"));

                hasSensitiveRows |= AddSensitiveEquipmentRow(
                    "Secondary WiFi Password",
                    GetEquipmentValue("Secondary WiFi Password", "Secondary Password"));
            }

            if (!hasSensitiveRows)
            {
                AccessSecuritySectionPanel.Children.Add(new TextBlock
                {
                    Text = "No data",
                    FontStyle = FontStyles.Italic,
                    Foreground = TryFindResource("TextSecondary") as Brush
                });
            }
        }

        private void AddSerializedDeviceSection(
            string title,
            string? model,
            string? serial,
            string swapLabel,
            bool showModelBesideSerial = false,
            bool usesCommunicationDeviceTypePicker = false)
        {
            if (_serializedDeviceSectionCount > 0)
                SerializedDevicesPanel.Children.Add(CreateSerializedDeviceSeparator());

            var oldSerial = string.IsNullOrWhiteSpace(serial)
                ? string.Empty
                : serial.Trim();

            var replacementKey = BuildReplacementEntryKey(swapLabel, oldSerial);
            var replacementAlreadyAdded = _activeReplacementEntryKeys.Contains(replacementKey);

            var section = new StackPanel();

            var headerGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(titleBlock, 0);

            var swapButton = new Button
            {
                Content = CreateSwapButtonContent(),
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 26,
                MinWidth = 82,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !replacementAlreadyAdded,
                ToolTip = replacementAlreadyAdded
                    ? "A replacement entry already exists for this device."
                    : "Create replacement entry",
                Tag = new SerializedDeviceInfo
                {
                    Label = swapLabel,
                    OldSerial = oldSerial,
                    ReplacementKey = replacementKey,
                    UsesCommunicationDeviceTypePicker = usesCommunicationDeviceTypePicker
                }
            };

            swapButton.Click += SwapSerializedDeviceButton_Click;
            Grid.SetColumn(swapButton, 1);

            headerGrid.Children.Add(titleBlock);
            headerGrid.Children.Add(swapButton);

            section.Children.Add(headerGrid);

            if (showModelBesideSerial)
            {
                section.Children.Add(CreateSideBySideEquipmentValues(
                    "Model",
                    string.IsNullOrWhiteSpace(model) ? "No data" : model.Trim(),
                    "Serial Number",
                    string.IsNullOrWhiteSpace(oldSerial) ? "No data" : oldSerial));
            }
            else
            {
                section.Children.Add(CreateStackedEquipmentValue(
                    "Serial Number",
                    string.IsNullOrWhiteSpace(oldSerial)
                        ? "Not returned by database"
                        : oldSerial));
            }

            SerializedDevicesPanel.Children.Add(section);
            _serializedDeviceSectionCount++;
        }

        private FrameworkElement CreateSerializedDeviceSeparator()
        {
            return new Border
            {
                Height = 1,
                Margin = new Thickness(0, 10, 0, 10),
                Background = TryFindResource("SurfaceBorder") as Brush
            };
        }

        private FrameworkElement CreateSideBySideEquipmentValues(
            string leftLabel,
            string leftValue,
            string rightLabel,
            string rightValue)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 2)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = CreateValueStack(leftLabel, leftValue);
            Grid.SetColumn(left, 0);

            var right = CreateValueStack(rightLabel, rightValue);
            Grid.SetColumn(right, 2);

            grid.Children.Add(left);
            grid.Children.Add(right);

            return grid;
        }

        private FrameworkElement CreateStackedEquipmentValue(string label, string value)
        {
            return CreateValueStack(label, value, new Thickness(0, 0, 0, 2));
        }

        private FrameworkElement CreateValueStack(string label, string value)
        {
            return CreateValueStack(label, value, new Thickness(0));
        }

        private FrameworkElement CreateValueStack(string label, string value, Thickness margin)
        {
            var stack = new StackPanel
            {
                Margin = margin
            };

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = TryFindResource("TextPrimary") as Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            });

            return stack;
        }

        private bool AddSensitiveEquipmentRow(string label, string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            var cleanValue = rawValue.Trim();

            var displayValue = _showSensitiveEquipmentValues
                ? cleanValue
                : MaskSensitiveValue(cleanValue);

            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(0, 0, 0, 8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            var valuePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 0)
            };

            valuePanel.Children.Add(new TextBlock
            {
                Text = displayValue,
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            });

            var copyIcon = CreateTinyInlineCopyIcon($"Copy {label}");
            var copyVisualVersion = 0;

            copyIcon.MouseLeftButtonUp += async (_, _) =>
            {
                var copied = await TryCopyToClipboardAsync(cleanValue);

                if (!copied)
                {
                    copyIcon.ToolTip = "Could not copy. Try again.";
                    return;
                }

                var thisVersion = ++copyVisualVersion;

                copyIcon.Text = CheckGlyph;
                copyIcon.ToolTip = "Copied!";

                await Task.Delay(TimeSpan.FromSeconds(3));

                if (copyVisualVersion == thisVersion)
                {
                    copyIcon.Text = CopyGlyph;
                    copyIcon.ToolTip = $"Copy {label}";
                }
            };

            valuePanel.Children.Add(copyIcon);
            stack.Children.Add(valuePanel);

            border.Child = stack;
            AccessSecuritySectionPanel.Children.Add(border);

            return true;
        }

        private void AddReplacementEntryRow(
            string? label = null,
            string? oldSerial = null,
            bool allowCustomLabel = true,
            bool usesCommunicationDeviceTypePicker = false,
            string? replacementKey = null)
        {
            if (ReplacementEntriesPanel is null)
                return;

            if (!CanAddReplacementEntry())
                return;

            var cleanLabel = (label ?? string.Empty).Trim();
            var cleanOldSerial = (oldSerial ?? string.Empty).Trim();

            var outerBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SurfaceBg") as Brush
            };

            outerBorder.Tag = new ReplacementEntryRowTag
            {
                Label = cleanLabel,
                UsesCommunicationDeviceTypePicker = usesCommunicationDeviceTypePicker,
                ReplacementKey = replacementKey
            };

            var root = new Grid();

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = usesCommunicationDeviceTypePicker && !string.IsNullOrWhiteSpace(cleanLabel)
                ? $"{cleanLabel} Replacement"
                : "Replacement Entry";

            var titleBlock = new TextBlock
            {
                Text = headerText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(titleBlock, 0);

            var removeButton = new Button
            {
                Content = CreateTrashButtonContent(),
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 30,
                Width = 36,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Remove entry"
            };

            removeButton.Click += (_, _) =>
            {
                ReplacementEntriesPanel.Children.Remove(outerBorder);

                if (!string.IsNullOrWhiteSpace(replacementKey))
                {
                    _activeReplacementEntryKeys.Remove(replacementKey);
                    RefreshEquipmentCards();
                }
            };

            Grid.SetColumn(removeButton, 1);

            headerGrid.Children.Add(titleBlock);
            headerGrid.Children.Add(removeButton);

            Grid.SetRow(headerGrid, 0);
            root.Children.Add(headerGrid);

            // Fields
            var fieldsGrid = new Grid();
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fieldsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var firstField = usesCommunicationDeviceTypePicker
                ? CreateCommunicationDeviceTypePicker("Device Type")
                : CreateReplacementField("Item", cleanLabel, isReadOnly: !allowCustomLabel, fieldKey: "ReplacementItem");

            Grid.SetColumn(firstField, 0);

            var oldSerialField = CreateReplacementField(
                "Old Serial",
                cleanOldSerial,
                isReadOnly: false,
                fieldKey: "ReplacementOldSerial");

            Grid.SetColumn(oldSerialField, 2);

            var newSerialField = CreateReplacementField(
                "New Serial",
                string.Empty,
                isReadOnly: false,
                fieldKey: "ReplacementNewSerial");

            Grid.SetColumn(newSerialField, 4);

            fieldsGrid.Children.Add(firstField);
            fieldsGrid.Children.Add(oldSerialField);
            fieldsGrid.Children.Add(newSerialField);

            Grid.SetRow(fieldsGrid, 2);
            root.Children.Add(fieldsGrid);

            outerBorder.Child = root;
            ReplacementEntriesPanel.Children.Add(outerBorder);
        }

        private FrameworkElement CreateReplacementField(string label, string value, bool isReadOnly, string? fieldKey = null)
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush,
                Margin = new Thickness(0, 0, 0, 4)
            });

            stack.Children.Add(new TextBox
            {
                Text = value,
                Tag = fieldKey,
                IsReadOnly = isReadOnly,
                Style = (Style)FindResource("ModernWatermarkTextBox"),
                Height = 30,
                MinWidth = 180,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            });

            return stack;
        }

        private FrameworkElement CreateCommunicationDeviceTypePicker(string label)
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var comboBox = new ComboBox
            {
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEditable = false,
                Tag = "ReplacementDeviceType"
            };

            if (TryFindResource("ModernComboBoxStyle") is Style comboStyle)
                comboBox.Style = comboStyle;

            var names = _communicationDeviceTypes
                .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.DisplayName))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .Select(x => x.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
                names = FallbackCommunicationDeviceTypes.ToList();

            foreach (var name in names)
                comboBox.Items.Add(name);

            comboBox.SelectedIndex = names.Count > 0 ? 0 : -1;

            stack.Children.Add(comboBox);

            return stack;
        }

        private void SwapSerializedDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SerializedDeviceInfo info)
                return;

            if (!CanAddReplacementEntry())
                return;

            var replacementKey = string.IsNullOrWhiteSpace(info.ReplacementKey)
                ? BuildReplacementEntryKey(info.Label, info.OldSerial)
                : info.ReplacementKey;

            if (_activeReplacementEntryKeys.Contains(replacementKey))
                return;

            _activeReplacementEntryKeys.Add(replacementKey);

            button.IsEnabled = false;
            button.ToolTip = "A replacement entry already exists for this device.";

            AddReplacementEntryRow(
                label: info.Label,
                oldSerial: info.OldSerial,
                allowCustomLabel: false,
                usesCommunicationDeviceTypePicker: info.UsesCommunicationDeviceTypePicker,
                replacementKey: replacementKey);
        }

        private void AddReplacementEntryButton_Click(object sender, RoutedEventArgs e)
        {
            AddReplacementEntryRow(
                label: string.Empty,
                oldSerial: string.Empty,
                allowCustomLabel: true);
        }

        private void ToggleSensitiveEquipmentButton_Click(object sender, RoutedEventArgs e)
        {
            _showSensitiveEquipmentValues = !_showSensitiveEquipmentValues;

            if (ToggleSensitiveEquipmentButton is not null)
                ToggleSensitiveEquipmentButton.Content = _showSensitiveEquipmentValues ? "Hide" : "View";

            RefreshEquipmentCards();
        }

        private string? GetEquipmentValue(params string[] labels)
        {
            if (labels is null || labels.Length == 0)
                return null;

            var lines = SplitEquipmentLines(_equipmentText);

            foreach (var line in lines)
            {
                var parsed = ParseEquipmentEntry(line);
                if (!parsed.HasValue)
                    continue;

                foreach (var label in labels)
                {
                    if (string.Equals(parsed.Value.Label, label, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(parsed.Value.Value) ? null : parsed.Value.Value.Trim();
                }
            }

            return null;
        }

        private static List<string> SplitEquipmentLines(string? text)
        {
            return (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static (string Label, string Value)? ParseEquipmentEntry(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var idx = line.IndexOf(':');

            if (idx <= 0 || idx >= line.Length - 1)
                return null;

            var label = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                return null;

            return (label, value);
        }

        private static string MaskSensitiveValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string('•', Math.Max(8, value.Length));
        }

        public void SetCommunicationDeviceTypes(IEnumerable<CommunicationDeviceTypeDto>? deviceTypes)
        {
            _communicationDeviceTypes = (deviceTypes ?? Enumerable.Empty<CommunicationDeviceTypeDto>())
                .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.DisplayName))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

        private object CreateSwapButtonContent()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            panel.Children.Add(new TextBlock
            {
                Text = "⇄",
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Swap",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private object CreateTrashButtonContent()
        {
            return new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static string BuildReplacementEntryKey(string? label, string? oldSerial)
        {
            var cleanLabel = (label ?? string.Empty).Trim();
            var cleanOldSerial = (oldSerial ?? string.Empty).Trim();

            return $"{cleanLabel}|{cleanOldSerial}";
        }

        private bool CanAddReplacementEntry(bool showMessage = true)
        {
            var currentCount = ReplacementEntriesPanel?.Children.Count ?? 0;

            if (currentCount < MaxReplacementEntries)
                return true;

            if (showMessage)
            {
                MessageBox.Show(
                    $"You can only add up to {MaxReplacementEntries} replacement entries at one time.",
                    "Replacement Entries Limit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        private const string CopyGlyph = "\uE8C8";
        private const string CheckGlyph = "\uE73E";

        private async Task<bool> TryCopyToClipboardAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Clipboard can be temporarily locked by Windows, Teams, Excel, remote sessions, etc.
            // Retry a few times instead of crashing the app.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (COMException)
                {
                    await Task.Delay(60);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private TextBlock CreateTinyInlineCopyIcon(string tooltip)
        {
            return new TextBlock
            {
                Text = CopyGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Foreground = TryFindResource("TextSecondary") as Brush,
                Width = 16,
                Height = 16,
                Margin = new Thickness(6, 0, -8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        //End of Equipment

        
        //TOP Info Card
        private void RefreshTopAccessPanel()
        {
            
            TopIpATextBox.Text = GetTopInfoValue("TOP IP A");
            TopIpBTextBox.Text = GetTopInfoValue("TOP IP B");

            TopIpAStateTextBlock.Text = string.Empty;
            TopIpBStateTextBlock.Text = string.Empty;

            
            OpenTopIpAButton.IsEnabled = !string.IsNullOrWhiteSpace(TopIpATextBox.Text);
            OpenTopIpBButton.IsEnabled = !string.IsNullOrWhiteSpace(TopIpBTextBox.Text);

            TestTopPairButton.IsEnabled =
                !string.IsNullOrWhiteSpace(TopIpATextBox.Text) &&
                !string.IsNullOrWhiteSpace(TopIpBTextBox.Text);
        }

        private static string GetShortTopAccessTitle(string fullTitle)
        {
            if (string.IsNullOrWhiteSpace(fullTitle))
                return "TOP Access";

            var text = fullTitle.Trim();

            var parenIndex = text.IndexOf(" (", StringComparison.Ordinal);

            if (parenIndex > 0)
                return text[..parenIndex].Trim();

            return text;
        }

        private string GetTopInfoValue(string label)
        {
            var lines = (TopInfoTextBox.Text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    continue;

                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                return line[(idx + 1)..].Trim();
            }

            return string.Empty;
        }

        private static void OpenTopIpInBrowser(string? ipText)
        {
            var ip = (ipText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ip) || ip == "—")
                return;

            var url = $"https://{ip}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void OpenTopIpAButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopIpInBrowser(TopIpATextBox.Text);
        }

        private void OpenTopIpBButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopIpInBrowser(TopIpBTextBox.Text);
        }

        private void OpenTopTunnelButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTopTunnelRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void TestTopPairButton_Click(object sender, RoutedEventArgs e)
        {
            var ipA = TopIpATextBox.Text?.Trim();
            var ipB = TopIpBTextBox.Text?.Trim();

            ClearTopPairState();

            if (string.IsNullOrWhiteSpace(ipA) || string.IsNullOrWhiteSpace(ipB))
                return;

            TopIpAStateTextBlock.Text = "Testing...";
            TopIpBStateTextBlock.Text = "Testing...";

            var taskA = MeasureAveragePingMsAsync(ipA);
            var taskB = MeasureAveragePingMsAsync(ipB);

            await Task.WhenAll(taskA, taskB);

            var avgA = taskA.Result;
            var avgB = taskB.Result;

            ApplyTopPairState(avgA, avgB);
        }        

        private static async Task<double?> MeasureAveragePingMsAsync(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            using var ping = new Ping();
            var successfulTimes = new List<long>();

            for (var i = 0; i < 3; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 500);
                    if (reply.Status == IPStatus.Success)
                        successfulTimes.Add(reply.RoundtripTime);
                }
                catch
                {
                }
            }

            if (successfulTimes.Count == 0)
                return null;

            return successfulTimes.Average();
        }

        private void ApplyTopPairState(double? avgA, double? avgB)
        {
            if (avgA is null && avgB is null)
            {
                TopIpAStateTextBlock.Text = "No Reply";
                TopIpBStateTextBlock.Text = "No Reply";
                return;
            }

            if (avgA is not null && avgB is null)
            {
                TopIpAStateTextBlock.Text = "Active";
                TopIpBStateTextBlock.Text = "Passive";
                return;
            }

            if (avgA is null && avgB is not null)
            {
                TopIpAStateTextBlock.Text = "Passive";
                TopIpBStateTextBlock.Text = "Active";
                return;
            }

            if (avgA <= avgB)
            {
                TopIpAStateTextBlock.Text = "Active";
                TopIpBStateTextBlock.Text = "Passive";
            }
            else
            {
                TopIpAStateTextBlock.Text = "Passive";
                TopIpBStateTextBlock.Text = "Active";
            }
        }

        private void ClearTopPairState()
        {
            TopIpAStateTextBlock.Text = string.Empty;
            TopIpBStateTextBlock.Text = string.Empty;
        }



        //Tickets
        private void ApplyTicketInfo(string rawText)
        {
            TicketNotificationNameTextBlock.Text = GetTicketFieldValue(rawText, "Notification Name");
            TicketNotificationNumberTextBlock.Text = GetTicketFieldValue(rawText, "Notification #");
            TicketProblemIssueTextBlock.Text = GetTicketFieldValue(rawText, "Problem/Issue");
            TicketWorkOrderTextBlock.Text = GetTicketFieldValue(rawText, "Work Order");
            TicketWorkOrderTypeTextBlock.Text = GetTicketFieldValue(rawText, "Work Order Type");
            TicketAssignedToTextBlock.Text = GetTicketFieldValue(rawText, "Assigned To");
            TicketDateCreatedTextBlock.Text = GetTicketFieldValue(rawText, "Date Created");
            TicketStatusTextBlock.Text = GetTicketFieldValue(rawText, "Current Status");

            if (string.IsNullOrWhiteSpace(TicketNotificationNameTextBlock.Text))
                TicketNotificationNameTextBlock.Text = "No ticket data returned yet.";

            if (string.IsNullOrWhiteSpace(TicketNotificationNumberTextBlock.Text))
                TicketNotificationNumberTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketProblemIssueTextBlock.Text))
                TicketProblemIssueTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketWorkOrderTextBlock.Text))
                TicketWorkOrderTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketWorkOrderTypeTextBlock.Text))
                TicketWorkOrderTypeTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketAssignedToTextBlock.Text))
                TicketAssignedToTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketDateCreatedTextBlock.Text))
                TicketDateCreatedTextBlock.Text = "—";

            if (string.IsNullOrWhiteSpace(TicketStatusTextBlock.Text))
                TicketStatusTextBlock.Text = "—";

            ApplyTicketActionButtons();
            ApplyTicketStatusDisplay();
        }

        private static string GetTicketFieldValue(string rawText, string label)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            var lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    continue;

                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                return line[(idx + 1)..].Trim();
            }

            return string.Empty;
        }        

        private void RefreshTicketButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshTicketRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CopyTicketNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            CopyTicketValue(TicketNotificationNumberTextBlock.Text);
        }

        private void CopyTicketWorkOrderButton_Click(object sender, RoutedEventArgs e)
        {
            CopyTicketValue(TicketWorkOrderTextBlock.Text);
        }

        private static void CopyTicketValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return;

            Clipboard.SetText(value);
        }

        private void ApplyTicketActionButtons()
        {
            RequestTicketButton.Visibility = Visibility.Collapsed;
            RequestCapitalButton.Visibility = Visibility.Collapsed;
            RequestMaintenanceButton.Visibility = Visibility.Collapsed;

            var hasTicket = CurrentTicketId > 0 &&
                            !TicketNotificationNameTextBlock.Text.Equals(
                                "No ticket data returned yet.",
                                StringComparison.OrdinalIgnoreCase);

            var workOrderType = (TicketWorkOrderTypeTextBlock.Text ?? string.Empty).Trim();

            // No ticket populated: allow user to request a ticket.
            if (!hasTicket)
            {
                RequestTicketButton.Visibility = Visibility.Visible;
                TicketActionHintTextBlock.Text = "No ticket is associated with this site.";
                return;
            }

            // Maintenance ticket/order: allow request to Capital.
            if (workOrderType.Equals("Maintenance", StringComparison.OrdinalIgnoreCase) ||
                workOrderType.Equals("Maint", StringComparison.OrdinalIgnoreCase))
            {
                RequestCapitalButton.Visibility = Visibility.Visible;
                TicketActionHintTextBlock.Text = "Maintenance order loaded.";
                return;
            }

            // Capital ticket/order: allow request to Maintenance.
            if (workOrderType.Equals("Capital", StringComparison.OrdinalIgnoreCase) ||
                workOrderType.Equals("Cap", StringComparison.OrdinalIgnoreCase))
            {
                RequestMaintenanceButton.Visibility = Visibility.Visible;
                TicketActionHintTextBlock.Text = "Capital order loaded.";
                return;
            }

            TicketActionHintTextBlock.Text = "Ticket actions require a reason.";
        }
        
        private void ApplyTicketStatusDisplay()
        {
            TicketStatusBadge.ClearValue(Border.BackgroundProperty);
            TicketStatusBadge.ClearValue(Border.BorderBrushProperty);
            TicketStatusBadge.Background = Brushes.Transparent;
            TicketStatusBadge.BorderThickness = new Thickness(0);
        }

        private void RequestCapitalButton_Click(object sender, RoutedEventArgs e)
        {
            var reason = PromptForTicketActionReason("Request Capital");

            if (string.IsNullOrWhiteSpace(reason))
                return;

            TicketActionRequested?.Invoke(
                this,
                new TicketActionRequestedEventArgs(
                    action: "RequestCapital",
                    ticketId: CurrentTicketId,
                    reason: reason,
                    workOrderType: TicketWorkOrderTypeTextBlock.Text ?? string.Empty,
                    notification: TicketNotificationNumberTextBlock.Text ?? string.Empty,
                    workOrder: TicketWorkOrderTextBlock.Text ?? string.Empty));
        }

        private void RequestMaintenanceButton_Click(object sender, RoutedEventArgs e)
        {
            var reason = PromptForTicketActionReason("Request Maintenance");

            if (string.IsNullOrWhiteSpace(reason))
                return;

            TicketActionRequested?.Invoke(
                this,
                new TicketActionRequestedEventArgs(
                    action: "RequestMaintenance",
                    ticketId: CurrentTicketId,
                    reason: reason,
                    workOrderType: TicketWorkOrderTypeTextBlock.Text ?? string.Empty,
                    notification: TicketNotificationNumberTextBlock.Text ?? string.Empty,
                    workOrder: TicketWorkOrderTextBlock.Text ?? string.Empty));
        }

        private void RequestTicketButton_Click(object sender, RoutedEventArgs e)
        {
            var reason = PromptForTicketActionReason("Request Ticket");

            if (string.IsNullOrWhiteSpace(reason))
                return;

            TicketActionRequested?.Invoke(
                this,
                new TicketActionRequestedEventArgs(
                    action: "RequestTicket",
                    ticketId: CurrentTicketId,
                    reason: reason,
                    workOrderType: TicketWorkOrderTypeTextBlock.Text ?? string.Empty,
                    notification: TicketNotificationNumberTextBlock.Text ?? string.Empty,
                    workOrder: TicketWorkOrderTextBlock.Text ?? string.Empty));
        }

        private string? PromptForTicketActionReason(string actionTitle)
        {
            var dialog = new Window
            {
                Title = actionTitle,
                Width = 460,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this),
                Background = TryFindResource("AppBackground") as Brush
            };

            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel();

            header.Children.Add(new TextBlock
            {
                Text = actionTitle,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush
            });

            header.Children.Add(new TextBlock
            {
                Text = "Enter the reason for this request.",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            Grid.SetRow(header, 0);

            var reasonBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                MinHeight = 110
            };

            if (TryFindResource("ModernTextBox") is Style textBoxStyle)
                reasonBox.Style = textBoxStyle;

            Grid.SetRow(reasonBox, 2);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 92,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0)
            };

            if (TryFindResource("SecondaryButtonStyle") is Style secondaryStyle)
                cancelButton.Style = secondaryStyle;

            var submitButton = new Button
            {
                Content = "Continue",
                Width = 104,
                Height = 32,
                IsDefault = true
            };

            if (TryFindResource("PrimaryButtonStyle") is Style primaryStyle)
                submitButton.Style = primaryStyle;

            string? result = null;

            cancelButton.Click += (_, _) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            submitButton.Click += (_, _) =>
            {
                var reason = (reasonBox.Text ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(reason))
                {
                    MessageBox.Show(
                        dialog,
                        "Enter a reason before continuing.",
                        actionTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                result = reason;
                dialog.DialogResult = true;
                dialog.Close();
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(submitButton);

            Grid.SetRow(buttons, 4);

            root.Children.Add(header);
            root.Children.Add(reasonBox);
            root.Children.Add(buttons);

            dialog.Content = root;

            return dialog.ShowDialog() == true
                ? result
                : null;
        }


        //Write-Up Stuff
        private const string EquipmentWriteUpHeader = "----Equipment Replacements----";
        private const string PingWriteUpHeader = "----Ping Stats----";
        private const string SnmpWriteUpHeader = "----SNMP Polls----";
        private const string TicketWriteUpHeader = "----Ticket----";

        private void SubmitWriteUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildSubmitWriteUpText(out var finalWriteUpText))
                return;

            var confirmed = ShowWriteUpPreviewWindow(finalWriteUpText);

            if (!confirmed)
                return;

            WriteUpSubmitRequested?.Invoke(
                this,
                new WriteUpSubmitRequestedEventArgs(
                    finalWriteUpText,
                    true,
                    IncludePingStatsCheckBox.IsChecked == true,
                    IncludeSnmpStatsCheckBox.IsChecked == true));
        }

        public sealed class TicketActionRequestedEventArgs : EventArgs
        {
            public TicketActionRequestedEventArgs(
                string action,
                long ticketId,
                string reason,
                string workOrderType,
                string notification,
                string workOrder)
            {
                Action = action;
                TicketId = ticketId;
                Reason = reason;
                WorkOrderType = workOrderType;
                Notification = notification;
                WorkOrder = workOrder;
            }

            public string Action { get; }
            public long TicketId { get; }
            public string Reason { get; }
            public string WorkOrderType { get; }
            public string Notification { get; }
            public string WorkOrder { get; }
        }

        private bool IsTowerDashboard => string.Equals(
            EquipmentDashboardKind,
            SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Tower,
            StringComparison.OrdinalIgnoreCase);

        private bool TryBuildSubmitWriteUpText(out string finalWriteUpText)
        {
            finalWriteUpText = string.Empty;

            var sections = new List<string>();

            var timestampSection = $"[{DateTime.Now:MM-dd-yyyy HH:mm}]";
            sections.Add(timestampSection);

            var reasonText = BuildWriteUpReasonText();

            if (!string.IsNullOrWhiteSpace(reasonText))
                sections.Add($"Reason: {reasonText}");

            var manualWriteUp = (WriteUpTextBox.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(manualWriteUp))
                sections.Add(manualWriteUp);

            if (!TryGetEquipmentReplacementLines(out var equipmentLines))
                return false;

            if (equipmentLines.Count > 0)
            {
                sections.Add(BuildSimpleWriteUpSection(
                    EquipmentWriteUpHeader,
                    equipmentLines));
            }

            var pingSection = string.Empty;

            if (IncludePingStatsCheckBox.IsChecked == true)
            {
                pingSection = IsTowerDashboard
                    ? GetTowerPingStatsForWriteUp()
                    : (PingStatsProvider?.Invoke()?.Trim() ?? string.Empty);

                if (string.IsNullOrWhiteSpace(pingSection))
                {
                    MessageBox.Show(
                        "Ping stats were selected, but no ping results are available yet.",
                        "Submit Write-Up",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                if (!IsTowerDashboard)
                    pingSection = AppendAssociatedTopToPingStats(pingSection);

                pingSection = StripLeadingWriteUpHeader(pingSection, "Ping Stats:");

                if (!string.IsNullOrWhiteSpace(pingSection))
                {
                    sections.Add(BuildSimpleWriteUpSection(
                        PingWriteUpHeader,
                        pingSection));
                }
            }

            var snmpSection = string.Empty;

            if (IncludeSnmpStatsCheckBox.IsChecked == true)
            {
                snmpSection = BuildSnmpStatsWriteUpSection();

                if (string.IsNullOrWhiteSpace(snmpSection))
                {
                    MessageBox.Show(
                        "SNMP stats were selected, but no useful SNMP results are available yet. Poll SNMP values first or uncheck SNMP stats.",
                        "Submit Write-Up",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                snmpSection = StripLeadingWriteUpHeader(snmpSection, "SNMP Polls:");

                if (!string.IsNullOrWhiteSpace(snmpSection))
                {
                    sections.Add(BuildSimpleWriteUpSection(
                        SnmpWriteUpHeader,
                        snmpSection));
                }
            }

            var ticketSection = BuildTicketWriteUpFooterSection();

            if (!string.IsNullOrWhiteSpace(ticketSection))
            {
                sections.Add(BuildSimpleWriteUpSection(
                    TicketWriteUpHeader,
                    ticketSection));
            }

            var cnpTechFooter = BuildCnpTechFooterSection();

            if (!string.IsNullOrWhiteSpace(cnpTechFooter))
                sections.Add(cnpTechFooter);

            finalWriteUpText = string.Join(
                Environment.NewLine + Environment.NewLine,
                sections.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (string.IsNullOrWhiteSpace(finalWriteUpText))
            {
                MessageBox.Show(
                    "There is no write-up content to submit.",
                    "Submit Write-Up",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }

            return true;
        }

        private static string BuildSimpleWriteUpSection(string header, IEnumerable<string> lines)
        {
            var cleanLines = lines
                .Select(x => (x ?? string.Empty).TrimEnd())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (cleanLines.Count == 0)
                return string.Empty;

            return header + Environment.NewLine + string.Join(Environment.NewLine, cleanLines);
        }

        private static string BuildSimpleWriteUpSection(string header, string body)
        {
            body = (body ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            return header + Environment.NewLine + body;
        }

        private static string StripLeadingWriteUpHeader(string text, string header)
        {
            text = (text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .ToList();

            if (lines.Count == 0)
                return text;

            if (string.Equals(lines[0].Trim(), header.Trim(), StringComparison.OrdinalIgnoreCase))
                lines.RemoveAt(0);

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);

            return string.Join(Environment.NewLine, lines).Trim();
        }

        private string BuildWriteUpReasonText()
        {
            // Prefer the real Problem/Issue field if it exists.
            var problem = GetNamedTextValue(
                "TicketProblemTextBlock",
                "TicketIssueTextBlock",
                "TicketProblemIssueTextBlock",
                "TicketProblemIssueValueTextBlock");

            if (!string.IsNullOrWhiteSpace(problem))
                return CleanTicketReferenceValue(problem);

            // Fallback: do NOT use the manual write-up as the reason anymore.
            return string.Empty;
        }

        private string BuildCnpTechFooterSection()
        {
            var techName = CleanTicketReferenceValue(CurrentCnpTechName);

            if (string.IsNullOrWhiteSpace(techName))
                return string.Empty;

            return "----------------------------" +
                   Environment.NewLine +
                   $"CNP Techs: {techName}";
        }

        private string BuildTicketWriteUpFooterSection()
        {
            var lines = new List<string>();

            var notificationName = CleanTicketReferenceValue(GetNamedTextValue(
                "TicketNotificationNameTextBlock",
                "NotificationNameTextBlock"));

            var notification = CleanTicketReferenceValue(GetNamedTextValue(
                "TicketNotificationNumberTextBlock",
                "NotificationNumberTextBlock"));

            var workOrder = CleanTicketReferenceValue(GetNamedTextValue(
                "TicketWorkOrderTextBlock",
                "WorkOrderTextBlock"));

            if (!string.IsNullOrWhiteSpace(notificationName))
                lines.Add(notificationName);

            if (!string.IsNullOrWhiteSpace(notification))
                lines.Add($"Notification: {notification}");

            if (!string.IsNullOrWhiteSpace(workOrder))
                lines.Add($"Work Order: {workOrder}");

            return lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines);
        }

        private string GetNamedTextValue(params string[] names)
        {
            foreach (var name in names)
            {
                if (FindName(name) is TextBlock textBlock)
                {
                    var value = CleanTicketReferenceValue(textBlock.Text);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                if (FindName(name) is TextBox textBox)
                {
                    var value = CleanTicketReferenceValue(textBox.Text);

                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return string.Empty;
        }

        private string GetTowerPingStatsForWriteUp()
        {
            var lines = new List<string>
            {
                "Ping Stats:"
            };

            foreach (var sector in _towerPingCards)
            {
                var sectorLines = new List<string>();

                foreach (var endpoint in sector.Endpoints)
                {
                    var summary = endpoint.SummaryTextBlock?.Text?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(summary) ||
                        summary.Equals("Ready.", StringComparison.OrdinalIgnoreCase) ||
                        summary.Equals("Testing...", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    sectorLines.Add($"{endpoint.Label} ({endpoint.IpAddress}) - {summary.TrimEnd('.')}");
                }

                if (sectorLines.Count == 0)
                    continue;

                if (lines.Count > 1)
                    lines.Add(string.Empty);

                lines.Add($"Sector {sector.Sector}:");
                lines.AddRange(sectorLines);
            }

            return lines.Count > 1
                ? string.Join(Environment.NewLine, lines)
                : string.Empty;
        }

        private static string CleanTicketReferenceValue(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text) ||
                text == "—" ||
                text.Equals("No ticket data returned yet.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return text;
        }

        private bool TryGetEquipmentReplacementLines(out List<string> lines)
        {
            lines = new List<string>();

            if (ReplacementEntriesPanel is null)
                return true;

            foreach (var child in ReplacementEntriesPanel.Children)
            {
                if (child is not Border rowBorder)
                    continue;

                if (rowBorder.Tag is not ReplacementEntryRowTag rowTag)
                    continue;

                var entry = GetEquipmentReplacementEntry(rowBorder, rowTag);

                var isCompletelyBlank =
                    string.IsNullOrWhiteSpace(entry.Item) &&
                    string.IsNullOrWhiteSpace(entry.OldSerial) &&
                    string.IsNullOrWhiteSpace(entry.NewSerial);

                if (isCompletelyBlank)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.Item))
                {
                    MessageBox.Show(
                        "One replacement entry is missing an item/device type.",
                        "Equipment Replacement",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.NewSerial))
                {
                    MessageBox.Show(
                        $"Enter the new serial number for {entry.Item}.",
                        "Equipment Replacement",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return false;
                }

                lines.Add(BuildEquipmentReplacementLine(entry));
            }

            return true;
        }

        private static string BuildEquipmentReplacementLine(EquipmentReplacementWriteUpEntry entry)
        {
            var item = entry.UsesCommunicationDeviceTypePicker
                ? FriendlyReplacementItemLabel(entry.Item)
                : FriendlyReplacementItemLabel(entry.Item);

            var oldSerial = entry.OldSerial.Trim();
            var newSerial = entry.NewSerial.Trim();

            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(oldSerial))
                lines.Add($"Found {item} SN: {oldSerial}");

            lines.Add($"Left {item} SN: {newSerial}");

            return string.Join(Environment.NewLine, lines);
        }

        private EquipmentReplacementWriteUpEntry GetEquipmentReplacementEntry(Border rowBorder, ReplacementEntryRowTag rowTag)
        {
            var item = rowTag.UsesCommunicationDeviceTypePicker
                ? GetTaggedComboBoxValue(rowBorder, "ReplacementDeviceType")
                : GetTaggedTextBoxValue(rowBorder, "ReplacementItem");

            return new EquipmentReplacementWriteUpEntry
            {
                SlotLabel = rowTag.Label,
                UsesCommunicationDeviceTypePicker = rowTag.UsesCommunicationDeviceTypePicker,
                Item = FriendlyReplacementItemLabel(item),
                OldSerial = GetTaggedTextBoxValue(rowBorder, "ReplacementOldSerial"),
                NewSerial = GetTaggedTextBoxValue(rowBorder, "ReplacementNewSerial")
            };
        }        

        private static string FriendlyReplacementItemLabel(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.EndsWith(" SN", StringComparison.OrdinalIgnoreCase))
                text = text[..^3].Trim();

            if (string.Equals(text, "Primary Communications", StringComparison.OrdinalIgnoreCase))
                return "Primary Communications";

            if (string.Equals(text, "Secondary Communications", StringComparison.OrdinalIgnoreCase))
                return "Secondary Communications";

            return text;
        }

        private bool _snmpCategoryOptionsInitialized;

        private void IncludeSnmpStatsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var includeSnmp = IncludeSnmpStatsCheckBox.IsChecked == true;

            SnmpCategoryOptionsPanel.Visibility = includeSnmp
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (includeSnmp && !_snmpCategoryOptionsInitialized)
            {
                IncludeSnmpAdminCheckBox.IsChecked = true;
                IncludeSnmpConfigCheckBox.IsChecked = true;
                IncludeSnmpStatsCategoryCheckBox.IsChecked = true;

                _snmpCategoryOptionsInitialized = true;
            }
        }

        private HashSet<string> GetSelectedSnmpWriteUpCategories()
        {
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (IncludeSnmpAdminCheckBox.IsChecked == true)
                categories.Add("Admin");

            if (IncludeSnmpConfigCheckBox.IsChecked == true)
                categories.Add("Config");

            if (IncludeSnmpStatsCategoryCheckBox.IsChecked == true)
                categories.Add("Stats");

            return categories;
        }

        private string BuildSnmpStatsWriteUpSection()
        {
            if (SnmpCategoryItemsControl?.ItemsSource is not IEnumerable categories)
                return string.Empty;

            var selectedCategories = GetSelectedSnmpWriteUpCategories();

            if (selectedCategories.Count == 0)
                return string.Empty;

            var categoryOrder = new List<string>();
            var groupedLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var categoryObject in categories.Cast<object>())
            {
                var categoryName = GetObjectTextProperty(categoryObject, "Category");

                if (string.IsNullOrWhiteSpace(categoryName))
                    categoryName = "SNMP";

                categoryName = categoryName.Trim();

                if (!selectedCategories.Contains(categoryName))
                    continue;

                var rows = GetObjectEnumerableProperty(categoryObject, "Rows");

                if (rows is null)
                    continue;

                foreach (var row in rows)
                {
                    var label = GetObjectTextProperty(row, "Label");
                    var rawResult = GetObjectTextProperty(row, "ResultText");

                    if (string.IsNullOrWhiteSpace(label))
                        continue;

                    if (!IsUsefulSnmpResultText(rawResult))
                        continue;

                    var result = NormalizeSnmpResultForWriteUp(rawResult);

                    if (string.IsNullOrWhiteSpace(result))
                        continue;

                    var line = $"{label.Trim()}: {result}";

                    var seenKey = $"{categoryName}|{line}";
                    if (!seenLines.Add(seenKey))
                        continue;

                    if (!groupedLines.ContainsKey(categoryName))
                    {
                        groupedLines[categoryName] = new List<string>();
                        categoryOrder.Add(categoryName);
                    }

                    groupedLines[categoryName].Add(line);
                }
            }

            if (groupedLines.Count == 0)
                return string.Empty;

            var output = new List<string>
            {
                "SNMP Polls:"
            };

            foreach (var categoryName in categoryOrder)
            {
                if (!groupedLines.TryGetValue(categoryName, out var lines) || lines.Count == 0)
                    continue;

                output.Add(string.Empty);
                output.Add($"{categoryName}-");
                output.AddRange(lines);
            }

            return string.Join(Environment.NewLine, output);
        }

        private static string GetObjectTextProperty(object source, string propertyName)
        {
            var prop = source.GetType().GetProperty(propertyName);

            var value = prop?.GetValue(source);

            return value?.ToString()?.Trim() ?? string.Empty;
        }

        private static IEnumerable<object>? GetObjectEnumerableProperty(object source, string propertyName)
        {
            var prop = source.GetType().GetProperty(propertyName);

            if (prop?.GetValue(source) is IEnumerable enumerable)
                return enumerable.Cast<object>();

            return null;
        }

        private string AppendAssociatedTopToPingStats(string pingStats)
        {
            var top = (TopAccessTitleTextBlock.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(top) ||
                top.Equals("TOP Access", StringComparison.OrdinalIgnoreCase))
            {
                return pingStats;
            }

            return pingStats.TrimEnd() +
                   Environment.NewLine +
                   Environment.NewLine +
                   $"Associated TOP: {top}";
        }

        private static string GetTaggedTextBoxValue(DependencyObject root, string tag)
        {
            return FindVisualChildren<TextBox>(root)
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                ?.Text
                ?.Trim()
                ?? string.Empty;
        }

        private static string GetTaggedComboBoxValue(DependencyObject root, string tag)
        {
            var comboBox = FindVisualChildren<ComboBox>(root)
                .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

            if (comboBox?.SelectedItem is null)
                return string.Empty;

            return comboBox.SelectedItem.ToString()?.Trim() ?? string.Empty;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent)
            where T : DependencyObject
        {
            if (parent is null)
                yield break;

            var childCount = VisualTreeHelper.GetChildrenCount(parent);

            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                    yield return typedChild;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private bool ShowWriteUpPreviewWindow(string finalWriteUpText)
        {
            var dialog = new Window
            {
                Title = "Submit Write-Up Preview",
                Width = 760,
                Height = 580,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this),
                Background = TryFindResource("AppBackground") as Brush
            };

            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel();

            header.Children.Add(new TextBlock
            {
                Text = "Review Write-Up Before Submit",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush
            });

            header.Children.Add(new TextBlock
            {
                Text = "Confirm this is exactly what should be submitted to the ticket.",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = TryFindResource("TextSecondary") as Brush
            });

            Grid.SetRow(header, 0);

            var previewBox = new TextBox
            {
                Text = finalWriteUpText,
                AcceptsReturn = true,
                Height = double.NaN,
                VerticalAlignment = VerticalAlignment.Stretch,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsReadOnly = true,
                Padding = new Thickness(10),
                FontSize = 13
            };

            if (TryFindResource("ModernTextBox") is Style textBoxStyle)
                previewBox.Style = textBoxStyle;

            Grid.SetRow(previewBox, 2);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 94,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            if (TryFindResource("SecondaryButtonStyle") is Style secondaryStyle)
                cancelButton.Style = secondaryStyle;

            var confirmButton = new Button
            {
                Content = "Confirm",
                Width = 104,
                Height = 32,
                IsDefault = true
            };

            if (TryFindResource("PrimaryButtonStyle") is Style primaryStyle)
                confirmButton.Style = primaryStyle;

            cancelButton.Click += (_, _) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            confirmButton.Click += (_, _) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(confirmButton);

            Grid.SetRow(buttons, 4);

            root.Children.Add(header);
            root.Children.Add(previewBox);
            root.Children.Add(buttons);

            dialog.Content = root;

            return dialog.ShowDialog() == true;
        }
    }
}