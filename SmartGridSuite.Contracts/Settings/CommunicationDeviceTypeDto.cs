using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartGridSuite.Contracts.Settings
{
    public sealed class CommunicationDeviceTypeDto
    {
        public uint Id { get; set; }
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
