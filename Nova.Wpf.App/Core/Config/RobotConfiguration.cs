using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Core.Config
{
    public class RobotConfigurations
    {
        public string EnumName { get; set; }
        public string Name { get; set; }
        public string IPAddress { get; set; }
        public RobotPorts Ports { get; set; }
    }
}
