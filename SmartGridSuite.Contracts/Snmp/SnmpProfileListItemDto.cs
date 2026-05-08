namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpProfileListItemDto
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = "";
        public string DeviceFamily { get; set; } = "";
        public bool IsActive { get; set; }
        public int OidCount { get; set; }

        public string SnmpVersion { get; set; } = "v3";
    }
}