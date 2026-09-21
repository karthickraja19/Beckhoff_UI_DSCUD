using System;

namespace Master.Repository
{
    public class OEEData
    {
        public DateTime DateTime { get; set; }
        public double Availability { get; set; }
        public double Performance { get; set; }
        public double Quality { get; set; }
    }
}
