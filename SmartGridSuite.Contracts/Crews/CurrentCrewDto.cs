using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Crews
{
    public sealed class CurrentCrewDto
    {
        public string PrimaryTech { get; set; } = "";
        public List<string> SecondaryTechs { get; set; } = new();
        public string DisplayText { get; set; } = "";
    }
}