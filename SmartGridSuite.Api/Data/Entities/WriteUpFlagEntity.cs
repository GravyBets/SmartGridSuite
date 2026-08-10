#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class WriteUpFlagEntity
    {
        public uint Id { get; set; }

        public string DisplayName { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public bool IsTechnicianVisible { get; set; } = true;

        public bool IsSystem { get; set; }

        public string? SystemKey { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}