using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    public class GantryConfiguration
    {
        public string Name { get; set; }

        // Changed from Dictionary<string,string>
        // public List<string> Axes { get; set; }
        public List<_Axis> Axes { get; set; }
    }
    public class _Axis
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int SafePositionPriority { get; set; }
    }
}
