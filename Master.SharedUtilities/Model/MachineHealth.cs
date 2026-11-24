using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.SharedUtilities
{
    public class MachineHealth
    {
        public double Runtime { get; set; }
        public double Downtime { get; set; }
        public double Idletime { get; set; }
        public double Totaltime
        {
            get
            {
                return Runtime + Downtime + Idletime;   
            }
        }
    }
}
