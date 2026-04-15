using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class SnmpOidConfigDto
    {
        public ulong Id { get; set; }
        public string Category { get; set; } = "";
        public string Label { get; set; } = "";
        public string Oid { get; set; } = "";
        public string ValueType { get; set; } = "String";

        public bool IsWritable { get; set; }
        public bool ShowInWorkspace { get; set; } = true;
        public int SortOrder { get; set; }

        public string DecodeMode { get; set; } = "Raw";
        public bool ShowRawValueAlongsideDecoded { get; set; }
        public List<SnmpOidDecodeValueDto> DecodeValues { get; set; } = new();
    }
}