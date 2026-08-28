#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class ClientPresenceEntity
    {
        public long Id { get; set; }

        public string? EmployeeId { get; set; }

        public string? DisplayName { get; set; }

        public string? WindowsUser { get; set; }

        public string MachineName { get; set; } = "";

        public string ClientVersion { get; set; } = "";

        public string CurrentModule { get; set; } = "";

        public DateTime FirstSeenAtUtc { get; set; }

        public DateTime LastSeenAtUtc { get; set; }
    }
}