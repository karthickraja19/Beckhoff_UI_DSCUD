using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
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
    }
}
