using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    public class CardConfiguration
    {
        public string ConnectionId { get; set; }
        public string DBName { get; set; }
        public int PortNo { get; set; }
        public string ServoDUTName { get; set; }
        public string StepperDUTName { get; set; }
        public string VacuumDUTName { get; set; }
        public string CylinderDUTName { get; set; }
        public int InputCount { get; set; }
        public int OutputCount { get; set; }
    }
}
