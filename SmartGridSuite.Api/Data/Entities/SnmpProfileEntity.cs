using System;
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class SnmpProfileEntity
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

        public bool IsDeleted { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string SnmpVersion { get; set; } = "v3";

        public virtual ICollection<SnmpOidEntity> Oids { get; set; } = new List<SnmpOidEntity>();
    }
}