using SmartGridSuite.Client.Services;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SmartGridSuite.Client.Views
{
    public partial class AboutWindow : Window
    {
        private readonly ClientVersionService _versionService =
            ClientVersionService.Current;

        public AboutWindow()
        {
            InitializeComponent();

            InterfaceScaleService.SetIsEnabled(
                this,
                false);

            VersionTextBlock.Text =
                $"Installed version " +
                $"{ClientVersionService.GetInstalledVersionText()}";

            ApplyCompanyLogo();

            ThemeService.ThemeChanged +=
                ThemeService_ThemeChanged;

            Loaded +=
                AboutWindow_Loaded;

            Closed +=
                AboutWindow_Closed;
        }

        private void ApplyCompanyLogo()
        {
            CompanyLogoImage.Source =
                new BitmapImage(
                    ThemeService.CurrentCompanyLogoUri);
        }

        private void ThemeService_ThemeChanged(
            object? sender,
            EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(ApplyCompanyLogo));

                return;
            }

            ApplyCompanyLogo();
        }

        private async void AboutWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            VersionStatusTextBlock.Text =
                "Checking version status...";

            VersionDetailsTextBlock.Text = "";

            var result =
                await _versionService.CheckAsync(
                    forceRefresh: true);

            VersionStatusTextBlock.Text =
                result.Message;

            if (result.State == ClientVersionState.Unknown)
            {
                VersionDetailsTextBlock.Text =
                    $"Installed: {result.InstalledVersion}. " +
                    "The application can continue running.";

                return;
            }

            VersionDetailsTextBlock.Text =
                $"Installed: {result.InstalledVersion}   •   " +
                $"Latest: {result.LatestVersion}   •   " +
                $"Minimum supported: " +
                $"{result.MinimumSupportedVersion}";
        }

        private void ChangeLogLink_Click(object sender, RoutedEventArgs e)
        {
            var window =
                new ChangeLogWindow
                {
                    Owner = this
                };

            window.ShowDialog();
        }

        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private void AboutWindow_Closed(
            object? sender,
            EventArgs e)
        {
            ThemeService.ThemeChanged -=
                ThemeService_ThemeChanged;

            Loaded -=
                AboutWindow_Loaded;

            Closed -=
                AboutWindow_Closed;
        }
    }
}