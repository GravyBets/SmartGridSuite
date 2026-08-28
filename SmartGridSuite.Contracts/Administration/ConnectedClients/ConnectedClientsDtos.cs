#nullable enable
using System;
using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Administration.ConnectedClients
{
    public sealed class ClientHeartbeatRequest
    {
        public string EmployeeId { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string WindowsUser { get; set; } = "";

        public string MachineName { get; set; } = "";

        public string ClientVersion { get; set; } = "";

        public string CurrentModule { get; set; } = "";
    }

    public sealed class ConnectedClientDto
    {
        public long Id { get; set; }

        public string EmployeeId { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string WindowsUser { get; set; } = "";

        public string MachineName { get; set; } = "";

        public string ClientVersion { get; set; } = "";

        public string CurrentModule { get; set; } = "";

        public DateTime FirstSeenAtUtc { get; set; }

        public DateTime LastSeenAtUtc { get; set; }

        public bool IsOnline { get; set; }

        public bool IsOutdated { get; set; }
    }

    public sealed class ConnectedClientsResponse
    {
        public DateTime ServerTimeUtc { get; set; }

        public string LatestVersion { get; set; } = "";

        public int OnlineClientCount { get; set; }

        public int OutdatedClientCount { get; set; }

        public int VersionsInUseCount { get; set; }

        public List<ConnectedClientDto> Clients { get; set; }
            = new();
    }
}