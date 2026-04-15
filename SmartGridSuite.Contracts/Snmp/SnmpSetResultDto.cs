namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpSetResultDto
    {
        public bool Success { get; set; }
        public string TargetIp { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public string Label { get; set; } = "";
        public string Oid { get; set; } = "";
        public string DecodeMode { get; set; } = "";
        public string RequestedValue { get; set; } = "";
        public string RawValue { get; set; } = "";
        public string DisplayValue { get; set; } = "";
        public string? ErrorMessage { get; set; }
    }
}