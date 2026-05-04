#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class CommunicationDeviceTypeEntity
    {
        public uint Id { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}