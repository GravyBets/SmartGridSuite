using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private bool IsRangeExtenderDashboard => string.Equals(EquipmentDashboardKind, SmartGridSuite.Contracts.SiteDashboard.SiteDashboardKinds.Rx,
            StringComparison.OrdinalIgnoreCase);

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

        private void ApplyDashboardFeatureVisibility()
        {
            var isRx =
                IsRangeExtenderDashboard;

            var isTower =
                string.Equals(
                    EquipmentDashboardKind,
                    SmartGridSuite.Contracts.SiteDashboard
                        .SiteDashboardKinds.Tower,
                    StringComparison.OrdinalIgnoreCase);

            /*
             * LINEMAN MODE
             *
             * Lineman uses an allow-list:
             *
             *   Main
             *   Site History
             *   Range Extender / RX
             *   Equipment - RX sites only
             *   Portal / PingScreen
             *
             * Everything diagnostic or equipment-related stays hidden,
             * regardless of what kind of site was loaded.
             */
            if (IsLinemanAccessMode)
            {
                if (RxOverviewTabItem is not null)
                {
                    RxOverviewTabItem.Visibility = isRx
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                if (TowerOverviewTabItem is not null)
                    TowerOverviewTabItem.Visibility =
                        Visibility.Collapsed;

                if (EquipmentTabItem is not null)
                {
                    EquipmentTabItem.Visibility =
                        isRx
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                }

                if (SnmpTabItem is not null)
                    SnmpTabItem.Visibility =
                        Visibility.Collapsed;

                /*
                 * These controls belong to the diagnostic portions of
                 * the write-up workflow and are not useful to Lineman.
                 */
                if (IncludePingStatsCheckBox is not null)
                {
                    IncludePingStatsCheckBox.Visibility =
                        Visibility.Collapsed;

                    IncludePingStatsCheckBox.IsChecked =
                        false;
                }

                if (IncludeSnmpStatsCheckBox is not null)
                {
                    IncludeSnmpStatsCheckBox.Visibility =
                        Visibility.Collapsed;

                    IncludeSnmpStatsCheckBox.IsChecked =
                        false;
                }

                if (SnmpCategoryOptionsPanel is not null)
                {
                    SnmpCategoryOptionsPanel.Visibility =
                        Visibility.Collapsed;
                }

                var selectedKey = SelectedWorkspaceTabKey;

                var isRxOnlyLinemanTab =
                    string.Equals(
                        selectedKey,
                        "RxOverview",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        selectedKey,
                        "Equipment",
                        StringComparison.OrdinalIgnoreCase);

                if (!isRx &&
                    isRxOnlyLinemanTab)
                {
                    SetSelectedWorkspaceTab(
                        "TopWriteUp");
                }
                else if (!IsLinemanAllowedWorkspaceTab(
                             selectedKey))
                {
                    SetSelectedWorkspaceTab(
                        "TopWriteUp");
                }

                if (isRx &&
                    string.Equals(
                        SelectedWorkspaceTabKey,
                        "TopWriteUp",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedWorkspaceTab(
                        "RxOverview");
                }

                return;
            }

            /*
             * FULL DASHBOARD MODE
             *
             * Preserve existing Dispatcher / Field Technician behavior.
             */
            if (RxOverviewTabItem is not null)
            {
                RxOverviewTabItem.Visibility =
                    isRx
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            if (TowerOverviewTabItem is not null)
            {
                TowerOverviewTabItem.Visibility =
                    isTower
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            if (EquipmentTabItem is not null)
            {
                EquipmentTabItem.Visibility =
                    Visibility.Visible;
            }

            if (SnmpTabItem is not null)
            {
                SnmpTabItem.Visibility =
                    isRx
                        ? Visibility.Collapsed
                        : Visibility.Visible;
            }

            if (IncludePingStatsCheckBox is not null)
            {
                IncludePingStatsCheckBox.Visibility =
                    isRx
                        ? Visibility.Collapsed
                        : Visibility.Visible;

                if (isRx)
                    IncludePingStatsCheckBox.IsChecked = false;
            }

            if (IncludeSnmpStatsCheckBox is not null)
            {
                IncludeSnmpStatsCheckBox.Visibility =
                    isRx
                        ? Visibility.Collapsed
                        : Visibility.Visible;

                if (isRx)
                    IncludeSnmpStatsCheckBox.IsChecked = false;
            }

            if (SnmpCategoryOptionsPanel is not null &&
                isRx)
            {
                SnmpCategoryOptionsPanel.Visibility =
                    Visibility.Collapsed;
            }

            if (isRx)
            {
                var selectedKey =
                    SelectedWorkspaceTabKey;

                if (string.Equals(
                        selectedKey,
                        "SNMPTool",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        selectedKey,
                        "TopWriteUp",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedWorkspaceTab(
                        "RxOverview");
                }
            }

            if (isTower)
            {
                var selectedKey =
                    SelectedWorkspaceTabKey;

                if (string.Equals(
                        selectedKey,
                        "TopWriteUp",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedWorkspaceTab(
                        "TowerOverview");
                }
            }
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
            if (_suppressWriteUpTextChanged)
                return;

            _pendingWriteUpText = WriteUpTextBox.Text ?? string.Empty;

            _writeUpTextChangedDebounceTimer.Stop();
            _writeUpTextChangedDebounceTimer.Start();
        }

        private void FlushWriteUpTextChangedDebounce()
        {
            if (_suppressWriteUpTextChanged)
                return;

            _writeUpTextChangedDebounceTimer.Stop();

            _pendingWriteUpText = WriteUpTextBox.Text ?? string.Empty;

            WriteUpTextChanged?.Invoke(this, _pendingWriteUpText);
        }

        private void WriteUpTextChangedDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _writeUpTextChangedDebounceTimer.Stop();

            WriteUpTextChanged?.Invoke(this, _pendingWriteUpText);
        }
    }
}