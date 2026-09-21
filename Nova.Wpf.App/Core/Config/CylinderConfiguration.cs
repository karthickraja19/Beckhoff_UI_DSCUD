using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Core.Config
{
    public class CylinderConfiguration
    {
        public string EnumName { get; set; }
        public int ValveType { get; set; }
        public string Name { get; set; }
        public List<string> ExtendValves { get; set; }
        public List<string> RetractValves { get; set; }
        public List<string> ExtendSenses { get; set; }
        public List<string> RetractSenses { get; set; }
        public int Timeout { get; set; }
        public int StableCount { get; set; }
        public int ActionTime { get; set; }
        public int BothSenseTimeout { get; set; }
        public CylinderButtons Buttons { get; set; }
        public bool IsCycleTimeConfigured { get; set; }
        public long ExtendCycleTimeNormal { get; set; }
        public string ExtendCycleTimeWarning { get; set; }
        public long ExtendCycleTimeAlarm { get; set; }
        public long RetractCycleTimeNormal { get; set; }
        public string RetractCycleTimeWarning { get; set; }
        public long RetractCycleTimeAlarm { get; set; }
        public string StationName { get; set; }
    }
}
