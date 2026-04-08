using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.SiteDashboard;
using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardTestPaneView : UserControl
    {
        private readonly ApiClient _api = new("https://localhost:7140");
        private sealed record HistoryRowVm(string DateText, string TechsText, string SummaryText);

        public SiteDashboardTestPaneView()
        {
            InitializeComponent();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var siteId = (SiteIdTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                MessageBox.Show("Site ID is required.");
                return;
            }

            try
            {
                StatusTextBlock.Text = "Loading...";

                var result = await _api.GetSiteDashboardAsync(siteId);

                if (result is null)
                {
                    StatusTextBlock.Text = "No response returned.";
                    ClearOutput();
                    return;
                }

                SiteIdValueTextBlock.Text = result.SiteId;
                DashboardKindValueTextBlock.Text = result.DashboardKind;
                RouteSiteTypeValueTextBlock.Text = result.Route?.SiteType ?? "-";

                LoadTypedDashboardSummary(result);

                DataJsonTextBox.Text = result.Data is null
                    ? ""
                    : JsonSerializer.Serialize(result.Data, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                StatusTextBlock.Text = "Loaded.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Load failed.";
                ClearOutput();
                MessageBox.Show($"Failed to load site dashboard.\n\n{ex.Message}");
            }
        }

        private void LoadTypedDashboardSummary(SiteDashboardResponseDto result)
        {
            ClearTypedSummary();

            if (result.DashboardKind == SiteDashboardKinds.Dacs &&
                result.Data is JsonElement dacsJson)
            {
                var dacs = dacsJson.Deserialize<DacsSiteDashboardDto>();

                if (dacs is not null)
                {
                    DacsSummaryCard.Visibility = Visibility.Visible;
                    DacsSiteStatusTextBlock.Text = dacs.SiteStatus ?? "-";
                    DacsTopNameTextBlock.Text = dacs.TopName ?? "-";
                    DacsPrimaryIpTextBlock.Text = dacs.PrimaryCommsIp ?? "-";
                    DacsTunnelIpTextBlock.Text = dacs.TunnelIp ?? "-";
                    DacsRtuIpTextBlock.Text = dacs.RtuIp ?? "-";
                    return;
                }
            }

            if (result.DashboardKind == SiteDashboardKinds.AmsMr &&
                result.Data is JsonElement amsJson)
            {
                var ams = amsJson.Deserialize<AmsSiteDashboardDto>();

                if (ams is not null)
                {
                    AmsSummaryCard.Visibility = Visibility.Visible;
                    AmsSiteStatusTextBlock.Text = ams.SiteStatus ?? "-";
                    AmsPrimaryIpTextBlock.Text = ams.PrimaryCommsIp ?? "-";
                    AmsSecondaryWanIpTextBlock.Text = ams.SecondaryWanIp ?? "-";
                    AmsSecondaryLanIpTextBlock.Text = ams.SecondaryLanIp ?? "-";
                    AmsTopNameTextBlock.Text = ams.TopName ?? "-";
                    AmsSecondarySimTextBlock.Text = ams.SecondarySimNumber ?? "-";
                    return;
                }
            }

            if (result.DashboardKind == SiteDashboardKinds.Rx &&
                result.Data is JsonElement rxJson)
            {
                var rx = rxJson.Deserialize<RxSiteDashboardDto>();

                if (rx is not null)
                {
                    RxSummaryCard.Visibility = Visibility.Visible;
                    RxSiteStatusTextBlock.Text = rx.SiteStatus ?? "-";
                    RxMeterNumberTextBlock.Text = rx.MeterNumber ?? "-";
                    RxMacAddressTextBlock.Text = rx.MacAddress ?? "-";
                    RxPolePointTextBlock.Text = rx.PolePoint ?? "-";
                    RxTransformerGlnTextBlock.Text = rx.TransformerGln ?? "-";
                    RxCityTextBlock.Text = rx.City ?? "-";
                    return;
                }
            }

            if (result.DashboardKind == SiteDashboardKinds.Igsd &&
                result.Data is JsonElement igsdJson)
            {
                var igsd = igsdJson.Deserialize<IgsdSiteDashboardDto>();

                if (igsd is not null)
                {
                    IgsdSummaryCard.Visibility = Visibility.Visible;
                    IgsdSiteStatusTextBlock.Text = igsd.SiteStatus ?? "-";
                    IgsdPrimaryIpTextBlock.Text = igsd.PrimaryCommsIp ?? "-";
                    IgsdPrimaryWanIpTextBlock.Text = igsd.PrimaryWanIp ?? "-";
                    IgsdSecondaryWanIpTextBlock.Text = igsd.SecondaryWanIp ?? "-";
                    IgsdTopNameTextBlock.Text = igsd.TopName ?? "-";
                    IgsdCyberlockTextBlock.Text = igsd.CyberlockSerialNumber ?? "-";
                    IgsdTunnelPskTextBlock.Text = igsd.TunnelPsk ?? "-";
                }
            }
        }

        private void ClearTypedSummary()
        {
            DacsSummaryCard.Visibility = Visibility.Collapsed;
            DacsSiteStatusTextBlock.Text = "-";
            DacsTopNameTextBlock.Text = "-";
            DacsPrimaryIpTextBlock.Text = "-";
            DacsTunnelIpTextBlock.Text = "-";
            DacsRtuIpTextBlock.Text = "-";

            AmsSummaryCard.Visibility = Visibility.Collapsed;
            AmsSiteStatusTextBlock.Text = "-";
            AmsPrimaryIpTextBlock.Text = "-";
            AmsSecondaryWanIpTextBlock.Text = "-";
            AmsSecondaryLanIpTextBlock.Text = "-";
            AmsTopNameTextBlock.Text = "-";
            AmsSecondarySimTextBlock.Text = "-";

            RxSummaryCard.Visibility = Visibility.Collapsed;
            RxSiteStatusTextBlock.Text = "-";
            RxMeterNumberTextBlock.Text = "-";
            RxMacAddressTextBlock.Text = "-";
            RxPolePointTextBlock.Text = "-";
            RxTransformerGlnTextBlock.Text = "-";
            RxCityTextBlock.Text = "-";

            IgsdSummaryCard.Visibility = Visibility.Collapsed;
            IgsdSiteStatusTextBlock.Text = "-";
            IgsdPrimaryIpTextBlock.Text = "-";
            IgsdPrimaryWanIpTextBlock.Text = "-";
            IgsdSecondaryWanIpTextBlock.Text = "-";
            IgsdTopNameTextBlock.Text = "-";
            IgsdCyberlockTextBlock.Text = "-";
            IgsdTunnelPskTextBlock.Text = "-";
        }

        private void ClearOutput()
        {
            SiteIdValueTextBlock.Text = "-";
            DashboardKindValueTextBlock.Text = "-";
            RouteSiteTypeValueTextBlock.Text = "-";
            DataJsonTextBox.Text = "";
            ClearTypedSummary();
        }
    }
}