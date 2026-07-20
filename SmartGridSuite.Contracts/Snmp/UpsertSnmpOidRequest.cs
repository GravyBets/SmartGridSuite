using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Snmp
{
    public sealed class UpsertSnmpOidRequest
    {
        public ulong? Id { get; set; }
        public string Category { get; set; } = "";
        public string Label { get; set; } = "";
        public string Oid { get; set; } = "";
        public string ValueType { get; set; } = "String";
        public bool IsWritable { get; set; }
        public bool ShowInWorkspace { get; set; } = true;
        public int SortOrder { get; set; }

        public string DecodeMode { get; set; } = "Raw";

        // Formula used to convert raw radio response into display value.
        // Example: raw 75700125 with "x / 100000" displays 757.00125.
        public string? ReadFormula { get; set; }

        // Formula used to convert user-entered display value into raw radio SET value.
        // Example: 757.00125 with "x * 100000" sends 75700125.
        public string? WriteFormula { get; set; }

        // Optional number of decimal places for display.
        public int? DecimalPlaces { get; set; }

        // Optional display unit.
        public string? UnitLabel { get; set; }
        public bool ShowRawValueAlongsideDecoded { get; set; }
        public List<UpsertSnmpOidDecodeValueRequest> DecodeValues { get; set; } = new();
    }
}