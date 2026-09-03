using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration;

namespace SmartGridSuite.Client.Views.Administration.SystemHealth
{
    public partial class SystemHealthAdminView : UserControl
    {
        private readonly ApiClient _api;
        private readonly DispatcherTimer _refreshTimer;

        private bool _isRefreshing;

        public SystemHealthAdminView(ApiClient api)
        {
            InitializeComponent();

            _api = api;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };

            _refreshTimer.Tick += RefreshTimer_Tick;

            Loaded += SystemHealthAdminView_Loaded;
            Unloaded += SystemHealthAdminView_Unloaded;
        }

        private async void SystemHealthAdminView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            _refreshTimer.Start();

            await RefreshAsync();
        }

        private void SystemHealthAdminView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            _refreshTimer.Stop();
        }

        private async void RefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await RefreshAsync();
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (_isRefreshing)
                return;

            _isRefreshing = true;
            RefreshButton.IsEnabled = false;
            RefreshStatusTextBlock.Text =
                "Refreshing system health...";

            try
            {
                var health =
                    await _api.GetSystemHealthAsync();

                if (health is null)
                {
                    throw new InvalidOperationException(
                        "The API returned no system health data.");
                }

                ApplyHealth(health);

                RefreshStatusTextBlock.Text =
                    "System health refreshed successfully.";
            }
            catch (Exception ex)
            {
                RefreshStatusTextBlock.Text =
                    "Unable to refresh system health: " +
                    ex.Message;
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                _isRefreshing = false;
            }
        }

        private void ApplyHealth(SystemHealthDto health)
        {
            UpdatedTextBlock.Text =
                $"Updated {FormatDateTime(health.GeneratedAtUtc)}";

            ApplyStatus(
                ApplicationStatusBadge,
                ApplicationStatusTextBlock,
                health.Application.Status);

            ApplicationMessageTextBlock.Text =
                ValueOrDash(health.Application.Message);

            ApiVersionTextBlock.Text =
                ValueOrDash(health.Application.ApiVersion);

            ApiStartedTextBlock.Text =
                FormatDateTime(health.Application.StartedAtUtc);

            ApiUptimeTextBlock.Text =
                FormatDuration(
                    health.Application.UptimeSeconds);

            ApplicationDatabaseTextBlock.Text =
                health.Application.ApplicationDatabaseConnected
                    ? "Connected"
                    : "Unavailable";

            DatabaseResponseTextBlock.Text =
                health.Application
                    .ApplicationDatabaseResponseMilliseconds
                    .HasValue
                        ? health.Application
                              .ApplicationDatabaseResponseMilliseconds
                              .Value
                              .ToString("N0") +
                          " ms"
                        : "—";

            ApplyStatus(
                ParentDatabaseStatusBadge,
                ParentDatabaseStatusTextBlock,
                health.ParentDatabase.Status);

            ParentDataSourceTextBlock.Text =
                health.ParentDatabase.IsUsingCache
                    ? "Cached fallback data"
                    : health.ParentDatabase
                        .LastSuccessfulConnectionUtc
                        .HasValue
                            ? "Live Parent DB"
                            : "No live check recorded";

            ParentLastSuccessTextBlock.Text =
                FormatDateTime(
                    health.ParentDatabase
                        .LastSuccessfulConnectionUtc);

            ParentLastFailureTextBlock.Text =
                FormatDateTime(
                    health.ParentDatabase.LastFailureUtc);

            ParentUnavailableSinceTextBlock.Text =
                FormatDateTime(
                    health.ParentDatabase
                        .UnavailableSinceUtc);

            ParentFailureOperationTextBlock.Text =
                ValueOrDash(
                    health.ParentDatabase
                        .LastFailureOperation);

            ParentFailureMessageTextBlock.Text =
                ValueOrDash(
                    health.ParentDatabase
                        .LastFailureMessage);

            ApplyStatus(
                CacheStatusBadge,
                CacheStatusTextBlock,
                health.ParentDatabaseCache.Status);

            CacheMessageTextBlock.Text =
                ValueOrDash(
                    health.ParentDatabaseCache.Message);

            CacheLastRefreshedTextBlock.Text =
                FormatDateTime(
                    health.ParentDatabaseCache
                        .LastRefreshedUtc);

            CacheAgeTextBlock.Text =
                FormatAge(
                    health.ParentDatabaseCache.AgeHours);

            CacheSyncRunTextBlock.Text =
                ValueOrDash(
                    health.ParentDatabaseCache.SyncRunId);

            CacheCountsTextBlock.Text =
                $"{health.ParentDatabaseCache.SiteCount:N0} sites, " +
                $"{health.ParentDatabaseCache.TowerCount:N0} towers, " +
                $"{health.ParentDatabaseCache.SectorCount:N0} sectors";

            ApplyStatus(
                BackupStatusBadge,
                BackupStatusTextBlock,
                health.Backup.Status);

            BackupMessageTextBlock.Text =
                ValueOrDash(health.Backup.Message);

            BackupMountedTextBlock.Text =
                health.Backup.BackupDriveMounted
                    ? "Mounted"
                    : "Not mounted";

            BackupLastAttemptTextBlock.Text =
                FormatDateTime(
                    health.Backup.LastAttemptUtc);

            BackupLastSuccessTextBlock.Text =
                FormatDateTime(
                    health.Backup
                        .LastSuccessfulBackupUtc);

            BackupAgeTextBlock.Text =
                FormatAge(health.Backup.AgeHours);

            ServerStorageTextBlock.Text =
                FormatDrive(
                    health.Storage.ServerDrive);

            BackupStorageTextBlock.Text =
                FormatDrive(
                    health.Storage.BackupDrive);
        }

        private static void ApplyStatus(
            Border badge,
            TextBlock textBlock,
            string? status)
        {
            var displayStatus =
                string.IsNullOrWhiteSpace(status)
                    ? "Unknown"
                    : status.Trim();

            var color =
                displayStatus switch
                {
                    "Healthy" or "Connected" =>
                        Color.FromRgb(46, 173, 98),

                    "Warning" or "Using Cache" =>
                        Color.FromRgb(240, 160, 32),

                    "Critical" =>
                        Color.FromRgb(224, 82, 82),

                    "Running" =>
                        Color.FromRgb(76, 159, 230),

                    _ =>
                        Color.FromRgb(128, 138, 148)
                };

            var foreground =
                new SolidColorBrush(color);

            var background =
                new SolidColorBrush(
                    Color.FromArgb(
                        36,
                        color.R,
                        color.G,
                        color.B));

            textBlock.Text = displayStatus;
            textBlock.Foreground = foreground;

            badge.BorderBrush = foreground;
            badge.Background = background;
        }

        private static string FormatDateTime(
            DateTime? value)
        {
            if (!value.HasValue ||
                value.Value == default)
            {
                return "—";
            }

            var utc =
                value.Value.Kind ==
                DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(
                        value.Value,
                        DateTimeKind.Utc)
                    : value.Value.ToUniversalTime();

            return utc
                .ToLocalTime()
                .ToString("ddd M/d/yyyy h:mm:ss tt");
        }

        private static string FormatDuration(
            long totalSeconds)
        {
            var duration =
                TimeSpan.FromSeconds(
                    Math.Max(0, totalSeconds));

            if (duration.TotalDays >= 1)
            {
                return
                    $"{(int)duration.TotalDays}d " +
                    $"{duration.Hours}h " +
                    $"{duration.Minutes}m";
            }

            if (duration.TotalHours >= 1)
            {
                return
                    $"{duration.Hours}h " +
                    $"{duration.Minutes}m";
            }

            if (duration.TotalMinutes >= 1)
            {
                return
                    $"{duration.Minutes}m " +
                    $"{duration.Seconds}s";
            }

            return $"{duration.Seconds}s";
        }

        private static string FormatAge(double? ageHours)
        {
            if (!ageHours.HasValue)
                return "—";

            var hours =
                Math.Max(0, ageHours.Value);

            if (hours >= 48)
            {
                return
                    $"{hours / 24d:N1} days";
            }

            return $"{hours:N1} hours";
        }

        private static string FormatDrive(
            DriveHealthDto drive)
        {
            if (!drive.Available ||
                !drive.TotalBytes.HasValue ||
                !drive.FreeBytes.HasValue)
            {
                return "Unavailable";
            }

            var used =
                drive.UsedPercentage.HasValue
                    ? $" ({drive.UsedPercentage.Value:N1}% used)"
                    : "";

            return
                $"{FormatBytes(drive.FreeBytes.Value)} free of " +
                $"{FormatBytes(drive.TotalBytes.Value)}" +
                used;
        }

        private static string FormatBytes(long bytes)
        {
            const double unit = 1024d;

            if (bytes >= unit * unit * unit * unit)
            {
                return
                    $"{bytes / (unit * unit * unit * unit):N1} TB";
            }

            if (bytes >= unit * unit * unit)
            {
                return
                    $"{bytes / (unit * unit * unit):N1} GB";
            }

            if (bytes >= unit * unit)
            {
                return
                    $"{bytes / (unit * unit):N1} MB";
            }

            return $"{bytes / unit:N1} KB";
        }

        private static string ValueOrDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "—"
                : value.Trim();
        }
    }
}