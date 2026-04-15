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

        public bool IsDeleted { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual SnmpProfileEntity? SnmpProfile { get; set; }
        public virtual ICollection<SnmpOidDecodeValueEntity> DecodeValues { get; set; } = new List<SnmpOidDecodeValueEntity>();
    }
}