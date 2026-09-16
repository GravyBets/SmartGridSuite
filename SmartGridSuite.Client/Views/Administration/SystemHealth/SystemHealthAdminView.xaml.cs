using System.Windows;
using System.Threading;
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
        private CancellationTokenSource? _maintenanceCts;

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
            _maintenanceCts?.Cancel();
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

            SetBusy(true);
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
                SetBusy(false);
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

            // Buttons are always visible; no visibility toggling required.

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

        private void SetBusy(bool busy)
        {
            _isRefreshing = busy;
            RefreshButton.IsEnabled = !busy;
            RestartApiButton.IsEnabled = !busy;
            TestApiButton.IsEnabled = !busy;
            BackupCacheNowButton.IsEnabled = !busy;
        }

        private async void BackupCacheNowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_isRefreshing)
                return;

            var confirm =
                MessageBox.Show(
                    "Create a fresh Parent DB cache snapshot now?\n\n" +
                    "The existing snapshot will remain available unless " +
                    "the entire new snapshot completes successfully.",
                    "Backup Parent DB Cache",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusy(true);

            using var operation =
                new CancellationTokenSource();

            _maintenanceCts = operation;

            RefreshStatusTextBlock.Text =
                "Creating Parent DB cache snapshot...";

            try
            {
                var result =
                    await _api.RefreshParentCacheAsync(
                        operation.Token);

                ApplyHealth(result.Health);

                RefreshStatusTextBlock.Text =
                    result.Message;
            }
            catch (OperationCanceledException)
                when (operation.IsCancellationRequested)
            {
                //Switching away from System Health cancels the request.
            }
            catch (Exception ex)
            {
                RefreshStatusTextBlock.Text =
                    "Parent DB cache snapshot failed. " +
                    "The previous snapshot was kept. " +
                    ex.Message;
            }
            finally
            {
                _maintenanceCts = null;
                SetBusy(false);
            }
        }

        private async void RestartApiButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
                return;

            SetBusy(true);
            using var operation = new CancellationTokenSource();
            _maintenanceCts = operation;
            RestartApiRequest? request = null;
            try
            {
                var dialog = new RestartApiPasswordWindow { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() != true)
                    return;

                operation.Token.ThrowIfCancellationRequested();
                request = new RestartApiRequest { Password = dialog.TakePassword() };
                RefreshStatusTextBlock.Text = "Requesting API restart...";
                RestartApiResponse response;
                try
                {
                    response = await _api.RestartApiAsync(request, operation.Token);
                }
                finally
                {
                    request.Password = "";
                }

                if (!response.Accepted)
                    throw new InvalidOperationException(response.Message);

                RefreshStatusTextBlock.Text = "Restart accepted. Waiting for the API to return...";
                using var polling = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                polling.CancelAfter(TimeSpan.FromSeconds(90));

                while (!polling.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), polling.Token);
                        var health = await _api.GetSystemHealthAsync(polling.Token);
                        if (health != null &&
                            health.Application.StartedAtUtc > response.PreviousStartedAtUtc)
                        {
                            ApplyHealth(health);
                            RefreshStatusTextBlock.Text =
                                "API restart confirmed. System health has been refreshed.";
                            return;
                        }
                    }
                    catch (OperationCanceledException) when (polling.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ApiClient.ApiConnectionException)
                    {
                        // Expected briefly while the process is restarting.
                    }
                    catch (ApiClient.ApiException)
                    {
                        // Apache can return 502/503 until the API is listening again.
                    }
                }

                if (!operation.IsCancellationRequested)
                    RefreshStatusTextBlock.Text =
                        "Restart was accepted, but a new API start time was not confirmed within 90 seconds. " +
                        "Refresh health or check the VM service.";
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                // Leaving the pane stops waiting; it does not undo an accepted restart.
            }
            catch (Exception ex)
            {
                RefreshStatusTextBlock.Text = "Restart not confirmed: " + ex.Message;
            }
            finally
            {
                if (request != null)
                    request.Password = "";
                _maintenanceCts = null;
                SetBusy(false);
            }
        }

        private async void TestApiButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
                return;

            SetBusy(true);
            using var operation = new CancellationTokenSource();
            _maintenanceCts = operation;
            RefreshStatusTextBlock.Text = "Testing a live Parent DB connection...";
            try
            {
                var result = await _api.TestParentDatabaseAsync(operation.Token)
                    ?? throw new InvalidOperationException("The API returned no test result.");

                // Apply every card and timestamp on both success and failure.
                ApplyHealth(result.Health);
                RefreshStatusTextBlock.Text = result.Message;

                if (!result.Health.Application.ApplicationDatabaseConnected)
                    ConnectivityService.ReportDegraded("The SmartGridSuite application database is unavailable.");
                else if (result.Health.ParentDatabase.IsUsingCache)
                    ConnectivityService.ReportDegraded("Parent DB is unavailable. Cached fallback data will be used.");
                else
                    ConnectivityService.ReportOnline();
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // An unreachable API is not evidence that Parent DB itself failed.
                // Explicitly mark the prior view as stale rather than leave a green result.
                ApplyStatus(ParentDatabaseStatusBadge, ParentDatabaseStatusTextBlock, "Unknown");
                ParentDataSourceTextBlock.Text = "Not verified";
                UpdatedTextBlock.Text = "Test unavailable — other values are from the previous refresh";
                RefreshStatusTextBlock.Text = "Unable to complete Parent DB test: " + ex.Message;
            }
            finally
            {
                _maintenanceCts = null;
                SetBusy(false);
            }
        }
    }
}
