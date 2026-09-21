using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Repository
{
    public class AlarmData
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmDescription { get; set; }
        public string AlarmType { get; set; }
        public string Shift { get; set; }
    }
}