namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class UpsertSnmpOidDecodeValueRequest
    {
        public ulong? Id { get; set; }
        public string RawValue { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public int SortOrder { get; set; }
    }
}