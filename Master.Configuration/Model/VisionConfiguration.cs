using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    /// <summary>One entry of CCDs.json: the library's VisionProperties, property for property.</summary>
    public class VisionConfiguration : NetworkDeviceConfiguration
    {
        /// <summary>The string the PLC sends to trigger a read.</summary>
        public string TriggerCommand { get; set; }
    }
}
