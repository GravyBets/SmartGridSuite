using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.SiteDashboard;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private void WorkspaceView_OpenTopTunnelRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var ip = (session.TopTunnelIp ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ip) || ip == "—")
                return;

            var url = $"https://{ip}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void ApplyShellLayoutMode(SiteDashboardTabSession session)
        {
            var hideNetwork =
                string.Equals(session.DashboardKind, SiteDashboardKinds.Rx, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase);

            NetworkView.Visibility = hideNetwork ? Visibility.Collapsed : Visibility.Visible;

            NetworkColumn.Width = hideNetwork
                ? new GridLength(0)
                : new GridLength(340);

            NetworkGapColumn.Width = hideNetwork
                ? new GridLength(0)
                : new GridLength(8);
        }

        private void ApplyNetworkLabels(SiteDashboardTabSession session)
        {
            ApplyNetworkLabelsForDefault(NetworkView);

            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Dacs, StringComparison.OrdinalIgnoreCase))
            {
                NetworkView.PrimaryPingLabel = "Primary";
                NetworkView.LanPingLabel = "Gateway";
                NetworkView.SecondaryPingLabel = "RTU";
                return;
            }

            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Igsd, StringComparison.OrdinalIgnoreCase))
            {
                NetworkView.PrimaryPingLabel = "Primary";
                NetworkView.LanPingLabel = "LAN";
                NetworkView.SecondaryPingLabel = "Secondary";
            }
        }

        private static void ApplyNetworkLabelsForDefault(SiteDashboardNetworkView networkView)
        {
            networkView.PrimaryPingLabel = "Primary";
            networkView.LanPingLabel = "LAN";
            networkView.SecondaryPingLabel = "Secondary";
        }

        private static string BuildNetworkHeader(string? siteStatus, string? siteId)
        {
            var cleanSiteId = string.IsNullOrWhiteSpace(siteId) ? "Site" : siteId.Trim();

            if (string.IsNullOrWhiteSpace(siteStatus))
                return $"Site {cleanSiteId}";

            return $"Site {cleanSiteId} - {siteStatus.Trim()}";
        }

        //RENDER METHOD
        private void RenderSelectedSession()
        {
            WorkspaceView.StopTowerPings();
            EnsureInitialBlankTab();

            TopBarView.SetTabs(_sessions, _selectedSessionKey);

            var session = GetSelectedSession();
            if (session is null)
            {
                _renderingSession = true;
                try
                {
                    HidePoppedOutOverlay();
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

            WorkspaceView.CurrentCnpTechName = _currentCnpTechName;

            ApplyShellLayoutMode(session);
            _renderingSession = true;

            try
            {
                //Header area
                TopBarView.SetSelectedTab(session.SessionKey);
                TopBarView.SearchText = session.SearchText;
                TopBarView.AddressText = session.AddressText;
                TopBarView.CoordinatesText = session.CoordinatesText;

                if (string.IsNullOrWhiteSpace(TopBarView.StatusText))
                    TopBarView.StatusText = "Ready.";

                if (session.IsPoppedOut && !_isPopOutInstance)
                {
                    ShowPoppedOutOverlay(session);
                    return;
                }

                HidePoppedOutOverlay();

                //Networking
                NetworkView.Reset();
                ApplyNetworkLabels(session);
                NetworkView.SiteHeader = BuildNetworkHeader(session.SiteStatusText, session.HeaderText);

                var isIgsd = string.Equals(
                    session.DashboardKind,
                    SiteDashboardKinds.Igsd,
                    StringComparison.OrdinalIgnoreCase);

                NetworkView.IsIgsdMode = isIgsd;

                NetworkView.PrimaryIp = session.PrimaryIp;
                NetworkView.LanIp = session.LanIp;
                NetworkView.SecondaryIp = session.SecondaryIp;

                NetworkView.IgsdPrimaryRtuIp = session.IgsdPrimaryRtuIp;
                NetworkView.IgsdPrimaryCommsEthernetIp = session.IgsdPrimaryCommsEthernetIp;
                NetworkView.IgsdSecondaryCommsEthernetIp = session.IgsdSecondaryCommsEthernetIp;
                NetworkView.IgsdSecondaryRtuIp = session.IgsdSecondaryRtuIp;

                NetworkView.ApplyLayoutMode();
                NetworkView.RestorePingSessionState(session.NetworkPingState);

                //Main Workspace
                WorkspaceView.Reset();
                WorkspaceView.TopAccessTitle = session.TopAccessTitleText;
                WorkspaceView.TopInfoText = session.TopInfoText;

                WorkspaceView.TopTunnelIp = session.TopTunnelIp;

                WorkspaceView.CurrentTicketId = session.CurrentTicketId;
                WorkspaceView.TicketInfoText = session.TicketInfoText;
                WorkspaceView.WriteUpText = session.WriteUpText;
                WorkspaceView.RestoreSubmitOptionsSessionState(session.SubmitOptions);

                WorkspaceView.ShowPortalTab = session.ShowIgsdPortalTab;
                WorkspaceView.PortalUrl = session.IgsdPortalUrl;
                WorkspaceView.RangeExtenderLinkUrl = session.RangeExtenderLinkUrl;

                WorkspaceView.EquipmentDashboardKind = session.DashboardKind;
                WorkspaceView.EquipmentText = session.EquipmentText;
                WorkspaceView.SetHistoryRows(session.HistoryRows);

                WorkspaceView.TowerSummaryText = session.TowerSummaryText;
                WorkspaceView.SetTowerSectors(session.TowerSectors);
                WorkspaceView.RestoreTowerPingSessionState(session.TowerPingState);
                WorkspaceView.SetSelectedWorkspaceTab(session.SelectedWorkspaceTabKey);



                if (session.ShowIgsdPortalTab &&
                    string.Equals(session.SelectedWorkspaceTabKey, "Portal", StringComparison.OrdinalIgnoreCase))
                {
                    _ = WorkspaceView.NavigatePortalAsync();
                }


                WorkspaceView.SetSnmpContext(
                    session.SnmpSupported,
                    session.SnmpSupportMessage,
                    session.SnmpDeviceFamily,
                    session.SnmpProfileName,
                    session.PrimaryIp,
                    session.LanIp,
                    session.SecondaryIp,
                    session.SnmpTargetIp);

                if (string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase))
                {
                    WorkspaceView.SetSnmpTargetOptions(
                        BuildTowerSnmpTargets(session),
                        session.SnmpTargetIp);
                }

                WorkspaceView.SetSnmpProfiles(session.SnmpProfiles, session.SnmpProfileId);

                WorkspaceView.SetSnmpOids(session.SnmpOids, session.SnmpOidResults);
                WorkspaceView.RefreshEquipmentDisplay();

                WorkspaceView.RestoreEquipmentReplacementSessionEntries(session.EquipmentReplacementEntries);

                /*
                 * Check local pending submissions only after the selected dashboard session
                 * has been fully rendered.
                 */
                QueuePendingWriteUpRecoveryForRenderedSession(session);

                QueueLoadSiteNotesForRenderedSession(session);
            }
            finally
            {
                _renderingSession = false;
            }
        }

        private void QueueLoadSiteNotesForRenderedSession(SiteDashboardTabSession session)
        {
            var siteId = ResolveSiteNotesSiteId(session);

            _ = Dispatcher.InvokeAsync(async () =>
            {
                await WorkspaceView.LoadSiteNotesAsync(siteId);
            });
        }
                
        private void ShowPoppedOutOverlay(SiteDashboardTabSession session)
        {
            if (PoppedOutOverlay is null)
                return;

            TopBarView.SetPopOutButtonVisible(false);

            PoppedOutOverlay.Visibility = Visibility.Visible;

            if (PoppedOutOverlaySiteTextBlock is not null)
            {
                PoppedOutOverlaySiteTextBlock.Text =
                    "All open Site Dashboard tabs are currently open in a popped-out Site Dashboard window.";
            }
        }

        private void HidePoppedOutOverlay()
        {
            if (!_isPopOutInstance)
                TopBarView.SetPopOutButtonVisible(true);

            if (PoppedOutOverlay is not null)
                PoppedOutOverlay.Visibility = Visibility.Collapsed;
        }

        private void BringPoppedOutForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_poppedOutWindow is not null)
            {
                BringPopOutWindowForward(_poppedOutWindow);
                return;
            }

            foreach (var session in _sessions)
                session.IsPoppedOut = false;

            RenderSelectedSession();
        }
    }
}