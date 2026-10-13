using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    /// <summary>
    /// One entry of Vacuums.json: the library's VacuumProperties, property for property. As with
    /// the cylinders, the sensor and output names are PLC variable names under PlcIoGvl.
    /// </summary>
    public class VacuumConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// The vacuum's index in {DBName}.{VacuumDUTName}.vacuumdata[] - the PLC array starts at 1.
        /// </summary>
        public int Id { get; set; }

        public string VacuumSense { get; set; }
        public string VacuumOutput { get; set; }
        public string BlowOutput { get; set; }
    }
}
