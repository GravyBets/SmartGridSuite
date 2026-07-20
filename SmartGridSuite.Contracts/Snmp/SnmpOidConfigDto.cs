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

        // Formula decode settings.
        // These are configured per OID so different radios can scale values differently.
        public string? ReadFormula { get; set; }

        // Optional formula used when writing values back to the radio.
        // Example: displayed 757.00125 -> raw SET value 75700125 with "x * 100000".
        public string? WriteFormula { get; set; }

        // Optional formatting control after ReadFormula is applied.
        // Example: 5 displays 757.00125 instead of 757.0012500000.
        public int? DecimalPlaces { get; set; }

        // Optional display suffix.
        // Example: "MHz", "dBm", "%".
        public string? UnitLabel { get; set; }

        public List<SnmpOidDecodeValueDto> DecodeValues { get; set; } = new();
    }
}