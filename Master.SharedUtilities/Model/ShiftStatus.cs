using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.SharedUtilities.Model
{
    public class ShiftStatus
    {
        // this has to increased for each Passed count
        public int PassCount { get; set; }
        // this has to increased for each Failed count
        public int FailCount { get; set; }
        public int OverallCount { get {  return PassCount + FailCount; } }
        public double Yield { get; set; }
        public double OEE { get; set; }
        public double MTBF { get; set; }
        public double MTBA { get; set; }
        public double TossingRate { get; set; }
        public double RetestRate { get; set; }
        // will be showing the units produced for last 1 hour
        public int UPH { get; set; }
        //this has to be updated for every output 
        public double CycleTime { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
