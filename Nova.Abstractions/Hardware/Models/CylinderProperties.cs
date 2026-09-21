using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class CylinderProperties
    {
        public string Name { get; set; }
        public List<int> ExtendOutputs { get; set; }
        public List<int> RetractOutputs { get; set; }
        public List<int> ExtendSenses { get; set; }
        public List<int> RetractSenses { get; set; }
        public int ActionTime { get; set; }
        public int StableCount { get; set; }
        public int Timeout { get; set; }
        public int BothSenseTimeout { get; set; }
    }
}

