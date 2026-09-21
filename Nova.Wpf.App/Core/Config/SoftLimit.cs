using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Core.Config
{
    public class SoftLimit
    {
        public double Positive { get; set; }
        public double Negative { get; set; }
        public int Acceleration { get; set; }
        public int Deceleration { get; set; }
        public int Speed { get; set; }
    }
}
