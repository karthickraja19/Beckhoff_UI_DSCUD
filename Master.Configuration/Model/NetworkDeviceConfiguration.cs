using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    /// <summary>
    /// One entry of Scanners.json (and, with <see cref="VisionConfiguration"/>,
    /// CCDs.json): the library's NetworkDeviceProperties, property for property, with Id as the
    /// device's index in the PLC array. IPAddress and Port are for the console tabs to show -
    /// the PLC makes the connection, so the library does not take them.
    /// </summary>
    /// <remarks>
    /// A property left out of the file keeps the library's default for that kind of device.
    /// InstancePath is not here: the PLC holds every device under the card's one DBName, so it
    /// is {DBName}.{VisionDUTName} (or ScannerDUTName) from
    /// CardsConfiguration.json. DeviceType is not here either: the file says what kind of
    /// device it is.
    /// </remarks>
    public class NetworkDeviceConfiguration : SocketClientConfiguration
    {
        public int? ConnectTimeout { get; set; }
        public int? ResponseTimeout { get; set; }
        public int? PollIntervalMs { get; set; }
        public string StationName { get; set; }
    }
}
