using SmartGridSuite.Contracts.Administration;

namespace SmartGridSuite.Api.Services.SystemHealth
{
    public sealed class ServerHealthProbeService
    {
        private const string BackupStatusDirectory = "/var/lib/smartgridsuite-backup";

        private const string BackupMountPath = "/mnt/smartgridsuite-backup";

        private static readonly TimeSpan BackupWarningAge = TimeSpan.FromHours(36);

        public BackupHealthDto GetBackupHealth()
        {
            var scriptStatus =
                ReadFirstLine(
                    Path.Combine(
                        BackupStatusDirectory,
                        "last-status"));

            var lastAttemptUtc =
                ReadUtcDate(
                    Path.Combine(
                        BackupStatusDirectory,
                        "last-attempt-utc"));

            var lastSuccessUtc =
                ReadUtcDate(
                    Path.Combine(
                        BackupStatusDirectory,
                        "last-success-utc"));

            var isMounted = IsBackupDriveMounted();

            double? ageHours = null;

            if (lastSuccessUtc.HasValue)
            {
                ageHours =
                    Math.Max(
                        0,
                        (DateTime.UtcNow - lastSuccessUtc.Value)
                            .TotalHours);
            }

            var result = new BackupHealthDto
            {
                LastAttemptUtc = lastAttemptUtc,
                LastSuccessfulBackupUtc = lastSuccessUtc,
                AgeHours = ageHours,
                BackupDriveMounted = isMounted
            };

            if (!OperatingSystem.IsLinux() &&
                string.IsNullOrWhiteSpace(scriptStatus))
            {
                result.Status = "Unavailable";
                result.Message =
                    "Backup health is available on the Linux server.";

                return result;
            }

            if (!isMounted)
            {
                result.Status = "Critical";
                result.Message =
                    "The SmartGridSuite backup drive is not mounted.";

                return result;
            }

            if (string.Equals(
                    scriptStatus,
                    "Running",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Running";
                result.Message =
                    "The SmartGridSuite backup is currently running.";

                return result;
            }

            if (string.Equals(
                    scriptStatus,
                    "Failed",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Critical";
                result.Message =
                    "The most recent SmartGridSuite backup failed.";

                return result;
            }

            if (!lastSuccessUtc.HasValue)
            {
                result.Status = "Warning";
                result.Message =
                    "No successful backup timestamp is available.";

                return result;
            }

            if (DateTime.UtcNow - lastSuccessUtc.Value >
                BackupWarningAge)
            {
                result.Status = "Warning";
                result.Message =
                    "The most recent successful backup is more " +
                    "than 36 hours old.";

                return result;
            }

            if (string.Equals(
                    scriptStatus,
                    "Successful",
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Healthy";
                result.Message =
                    "The most recent SmartGridSuite backup " +
                    "completed successfully.";

                return result;
            }

            result.Status = "Unknown";
            result.Message =
                "The backup job returned an unrecognized status.";

            return result;
        }

        public StorageHealthDto GetStorageHealth()
        {
            var serverPath =
                Path.GetPathRoot(AppContext.BaseDirectory) ??
                "/";

            var backupMounted =
                IsBackupDriveMounted();

            return new StorageHealthDto
            {
                ServerDrive =
                    GetDriveHealth(serverPath),

                BackupDrive =
                    backupMounted
                        ? GetDriveHealth(BackupMountPath)
                        : new DriveHealthDto
                        {
                            Available = false
                        }
            };
        }

        private static bool IsBackupDriveMounted()
        {
            if (!OperatingSystem.IsLinux())
                return false;

            const string mountsFile = "/proc/self/mounts";

            try
            {
                if (!File.Exists(mountsFile))
                    return false;

                foreach (var line in File.ReadLines(mountsFile))
                {
                    var parts =
                        line.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 2 &&
                        string.Equals(
                            parts[1],
                            BackupMountPath,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static DriveHealthDto GetDriveHealth(string path)
        {
            try
            {
                var drive = new DriveInfo(path);

                if (!drive.IsReady ||
                    drive.TotalSize <= 0)
                {
                    return new DriveHealthDto
                    {
                        Available = false
                    };
                }

                var usedBytes =
                    drive.TotalSize -
                    drive.AvailableFreeSpace;

                return new DriveHealthDto
                {
                    Available = true,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    UsedPercentage =
                        usedBytes * 100d /
                        drive.TotalSize
                };
            }
            catch
            {
                return new DriveHealthDto
                {
                    Available = false
                };
            }
        }

        private static string? ReadFirstLine(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                return File.ReadLines(path)
                    .FirstOrDefault()
                    ?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? ReadUtcDate(string path)
        {
            var value = ReadFirstLine(path);

            if (!DateTimeOffset.TryParse(
                    value,
                    out var parsed))
            {
                return null;
            }

            return parsed.UtcDateTime;
        }
    }
}