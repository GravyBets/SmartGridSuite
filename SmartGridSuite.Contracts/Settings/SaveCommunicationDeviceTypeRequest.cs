using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartGridSuite.Contracts.Settings
{
    public sealed class SaveCommunicationDeviceTypeRequest
    {
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
