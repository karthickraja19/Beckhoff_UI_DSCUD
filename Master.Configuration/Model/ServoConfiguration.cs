using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    public class ServoConfiguration
    {
        public string EnumName { get; set; }
        public int CardId { get; set; }
        public string Name { get; set; }
        public int Id { get; set; }
        public int Pitch { get; set; }
        public int Pulse { get; set; }
        public int PPR { get; set; }
        public int Acceleration { get; set; }
        public int Deceleration { get; set; }
        public int HomingSpeed { get; set; }
        public int Speed { get; set; }
        public string HomingMethod { get; set; }
        public List<AxisPosition> ConfiguredPositions { get; set; }
        public SoftLimit SoftLimits { get; set; }
    }
}
