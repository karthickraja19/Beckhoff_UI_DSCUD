using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    public class CylinderConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }
        public string ExtendValves { get; set; }
        public string RetractValves { get; set; }
        public List<string> ExtendSenses { get; set; }
        public List<string> RetractSenses { get; set; }
    }
}
