using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class VacuumEventArgs : EventArgs
    {
        public string ComponentId { get; set; }
        public int VaccumOutput { get; set; }
        public int BlowOutput { get; set; }
        public DateTime Timestamp { get; }

        public VacuumEventArgs(string componentId, int vaccumOutput = -1, int blowOutput = -1)
        {
            ComponentId = componentId;
            VaccumOutput = vaccumOutput;
            BlowOutput = blowOutput;
            Timestamp = DateTime.UtcNow;
        }
    }
}


