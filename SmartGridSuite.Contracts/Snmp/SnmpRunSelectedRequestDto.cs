namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpRunSelectedRequestDto
    {
        public ulong ProfileId { get; set; }
        public ulong OidId { get; set; }
        public string TargetIp { get; set; } = "";
    }
}