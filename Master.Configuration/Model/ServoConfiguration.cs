using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Teal.Library.Contracts;

namespace Master.Configuration.Model
{
    /// <summary>
    /// One entry of Servos.json: the library's AxisProperties, property for property, plus the
    /// positions taught on the recipe screens, which only the HMI uses. Homed is not here - the
    /// library reads it from the PLC.
    /// </summary>
    public class ServoConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }

        /// <summary>The axis's index in the PLC's servo arrays.</summary>
        public int Id { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public MotorType Type { get; set; }

        public double Pulse { get; set; }
        public double Velocity { get; set; }
        public double Acceleration { get; set; }
        public double Deceleration { get; set; }
        public double PositiveLimit { get; set; }
        public double NegativeLimit { get; set; }

        /// <summary>Milliseconds to wait for a move to finish; 0 leaves the HMI's default, 60 s.</summary>
        public int Timeout { get; set; }

        /// <summary>The positions taught on the recipe screens. Not passed to the library.</summary>
        public List<AxisPosition> ConfiguredPositions { get; set; }
    }
}
