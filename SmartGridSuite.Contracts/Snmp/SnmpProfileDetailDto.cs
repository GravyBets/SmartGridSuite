using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpProfileDetailDto
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = "";
        public string DeviceFamily { get; set; } = "";
        public bool IsActive { get; set; }
        public bool IsDefaultForFamily { get; set; }

        public string? ReadCommunity { get; set; }
        public string? WriteCommunity { get; set; }
        public string? ContextName { get; set; }

        public string? UsmUser { get; set; }
        public string? AuthProtocol { get; set; }
        public string? AuthKey { get; set; }
        public string? PrivacyProtocol { get; set; }
        public string? PrivacyKey { get; set; }

        public int TimeoutMs { get; set; } = 1500;
        public int Retries { get; set; } = 1;

        public string SnmpVersion { get; set; } = "v3";

        public List<SnmpOidConfigDto> Oids { get; set; } = new();
    }
}