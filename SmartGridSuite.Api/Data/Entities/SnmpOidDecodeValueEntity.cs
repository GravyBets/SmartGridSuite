using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public partial class SnmpOidDecodeValueEntity
    {
        public ulong Id { get; set; }
        public ulong SnmpOidId { get; set; }

        public string RawValue { get; set; } = "";
        public string DisplayText { get; set; } = "";
        public int SortOrder { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual SnmpOidEntity? SnmpOid { get; set; }
    }
}