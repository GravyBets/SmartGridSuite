namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpSetSelectedRequestDto
    {
        public ulong ProfileId { get; set; }
        public ulong OidId { get; set; }
        public string TargetIp { get; set; } = "";
        public string Value { get; set; } = "";
    }
}