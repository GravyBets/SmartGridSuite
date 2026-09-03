namespace SmartGridSuite.Contracts.Administration
{
    public sealed class SystemHealthDto
    {
        public DateTime GeneratedAtUtc { get; set; }

        public ApplicationHealthDto Application { get; set; } = new();

        public ParentDatabaseHealthDto ParentDatabase { get; set; } = new();

        public ParentDatabaseCacheHealthDto ParentDatabaseCache { get; set; } = new();

        public BackupHealthDto Backup { get; set; } = new();

        public StorageHealthDto Storage { get; set; } = new();
    }

    public sealed class ApplicationHealthDto
    {
        public string Status { get; set; } = "Unknown";

        public string ApiVersion { get; set; } = "";

        public DateTime StartedAtUtc { get; set; }

        public long UptimeSeconds { get; set; }

        public bool ApplicationDatabaseConnected { get; set; }

        public long? ApplicationDatabaseResponseMilliseconds { get; set; }

        public string? Message { get; set; }
    }

    public sealed class ParentDatabaseHealthDto
    {
        public string Status { get; set; } = "Unknown";

        public bool IsUsingCache { get; set; }

        public DateTime? LastSuccessfulConnectionUtc { get; set; }

        public DateTime? LastFailureUtc { get; set; }

        public DateTime? UnavailableSinceUtc { get; set; }

        public string? LastFailureOperation { get; set; }

        public string? LastFailureMessage { get; set; }
    }

    public sealed class ParentDatabaseCacheHealthDto
    {
        public string Status { get; set; } = "Unknown";

        public DateTime? LastRefreshedUtc { get; set; }

        public double? AgeHours { get; set; }

        public string? SyncRunId { get; set; }

        public int SiteCount { get; set; }

        public int TowerCount { get; set; }

        public int SectorCount { get; set; }

        public string? Message { get; set; }
    }

    public sealed class BackupHealthDto
    {
        public string Status { get; set; } = "Unknown";

        public DateTime? LastAttemptUtc { get; set; }

        public DateTime? LastSuccessfulBackupUtc { get; set; }

        public double? AgeHours { get; set; }

        public bool BackupDriveMounted { get; set; }

        public string? Message { get; set; }
    }

    public sealed class StorageHealthDto
    {
        public DriveHealthDto ServerDrive { get; set; } = new();

        public DriveHealthDto BackupDrive { get; set; } = new();
    }

    public sealed class DriveHealthDto
    {
        public bool Available { get; set; }

        public long? TotalBytes { get; set; }

        public long? FreeBytes { get; set; }

        public double? UsedPercentage { get; set; }
    }
}