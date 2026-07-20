using System;
using System.Collections.Generic;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class SnmpOidEntity
    {
        public ulong Id { get; set; }
        public ulong SnmpProfileId { get; set; }

        public string Category { get; set; } = "";
        public string Label { get; set; } = "";
        public string Oid { get; set; } = "";
        public string ValueType { get; set; } = "String";

        public bool IsWritable { get; set; }
        public bool ShowInWorkspace { get; set; } = true;
        public int SortOrder { get; set; }

        public string DecodeMode { get; set; } = "Raw";
        public bool ShowRawValueAlongsideDecoded { get; set; }

        // Formula used to convert the raw value returned by the radio into
        // the value shown in the UI.
        //
        // Example:
        // Raw radio value: 75700125
        // ReadFormula: x / 100000
        // Displayed value: 757.00125
        public string? ReadFormula { get; set; }

        // Optional formula used when writing a value back to the radio.
        //
        // Example:
        // User enters: 757.00125
        // WriteFormula: x * 100000
        // Radio SET value: 75700125
        public string? WriteFormula { get; set; }

        // Optional number of decimal places to display after ReadFormula runs.
        //
        // Example:
        // DecimalPlaces = 5
        // Display: 757.00125
        public int? DecimalPlaces { get; set; }

        // Optional suffix shown with the decoded display value.
        //
        // Examples:
        // MHz
        // dBm
        // %
        public string? UnitLabel { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual SnmpProfileEntity? SnmpProfile { get; set; }
        public virtual ICollection<SnmpOidDecodeValueEntity> DecodeValues { get; set; } = new List<SnmpOidDecodeValueEntity>();
    }
}