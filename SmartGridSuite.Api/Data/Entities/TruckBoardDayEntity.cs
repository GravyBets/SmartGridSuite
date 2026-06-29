#nullable enable
using System;

namespace SmartGridSuite.Api.Data.Entities
{
    public sealed class TruckBoardDayEntity
    {
        public DateTime WorkDate { get; set; }

        public string InitializationSource { get; set; } = "";

        public DateTime? CarriedFromWorkDate { get; set; }

        public DateTime InitializedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}