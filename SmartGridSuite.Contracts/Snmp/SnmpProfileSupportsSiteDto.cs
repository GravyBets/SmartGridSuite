namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpProfileSupportsSiteDto
    {
        public bool SnmpSupported { get; set; }
        public string DeviceFamily { get; set; } = "";
        public ulong? ProfileId { get; set; }
        public string? ProfileName { get; set; }
    }
}