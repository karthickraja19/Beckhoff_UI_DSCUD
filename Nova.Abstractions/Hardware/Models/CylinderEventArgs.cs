using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class CylinderEventArgs : EventArgs
    {
        public string ComponentId { get; set; }
        public List<int> FailedExtendOutputs { get; set; }
        public List<int> FailedExtendSenses { get; set; }
        public List<int> FailedRetractOutputs { get; set; }
        public List<int> FailedRetractSenses { get; set; }
        public DateTime Timestamp { get; }

        public CylinderEventArgs(string componentId, List<int> failedExtendOutputs = null, List<int> failedExtendSenses = null, 
            List<int> failedRetractOutputs = null, List<int> failedRetractSenses = null)
        {
            ComponentId = componentId;
            FailedExtendOutputs = failedExtendOutputs;
            FailedExtendSenses = failedExtendSenses;
            FailedRetractOutputs = failedRetractOutputs;
            FailedRetractSenses = failedRetractSenses;
            Timestamp = DateTime.UtcNow;
        }
    }
}


