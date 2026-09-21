using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Core.Config
{
    public class VacuumConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }
        public string VacuumSense { get; set; }
        public string VacuumOutput { get; set; }
        public string VacuumContactType { get; set; }
        public string BlowOutput { get; set; }
        public string BlowContactType { get; set; }
        public int Timeout { get; set; }
        public int StableCount { get; set; }
        public VacuumButtons Buttons { get; set; }
        public bool IsCycleTimeConfigured { get; set; }
        public long CycleTimeNormal { get; set; }
        public string CycleTimeWarning { get; set; }
        public long CycleTimeAlarm { get; set; }
        public string StationName { get; set; }
    }
}
