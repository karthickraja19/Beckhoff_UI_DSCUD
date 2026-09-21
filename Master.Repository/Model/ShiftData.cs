using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Repository
{
    public class ShiftData
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public double RunTime { get; set; }
        public double DownTime { get; set; }
        public double IdleTime { get; set; }
        public double PlannedDownTime { get; set; } =0;
        public double BreakTime { get; set; } = 0;
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public double Yield { get; set; }
        public double OEE { get; set; }
        public double MTBF { get; set; }
        public double MTBA { get; set; }
        public double TossingRate  { get; set; }
        public double RetestRate { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}