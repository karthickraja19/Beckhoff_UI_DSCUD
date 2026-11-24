using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    /// <summary>
    /// One entry of Cylinders.json: the library's CylinderProperties, property for property.
    /// Id is the cylinder's E_Cylinders value, its index in the PLC's CylinderData. The valve
    /// and sensor names are Data\Config.csv points (the PLC's E_Outputs / E_Input names), used
    /// only for the board's labels.
    /// </summary>
    public class CylinderConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// The cylinder's index in {DBName}.{CylinderDUTName}.CylinderData[] - the PLC array
        /// starts at 1.
        /// </summary>
        public int Id { get; set; }

        /// <summary>The valve that extends the cylinder.</summary>
        public string ExtendOutput { get; set; }

        /// <summary>The valve that retracts it. Empty for a single-valve cylinder.</summary>
        public string RetractOutput { get; set; }

        public List<string> ExtendSenses { get; set; } = new List<string>();
        public List<string> RetractSenses { get; set; } = new List<string>();
    }
}
