using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Core.Config
{
    public class AxisPosition
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public int Speed { get; set; }
        public double Acceleration { get; set; }
        public double Deceleration { get; set; }
    }
}
