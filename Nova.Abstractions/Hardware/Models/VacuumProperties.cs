using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Hardware
{
    public class VacuumProperties
    {
        public string Name { get; set; }
        
        public int VacuumSense { get; set; }
      
        public int VacuumOutput { get; set; }

        public ContactType VacuumContactType { get; set; }

        public int BlowOutput { get; set; }

        public ContactType BlowContactType { get; set; }

        public int Timeout { get; set; }

        public int StableCount { get; set; }

    }
}

