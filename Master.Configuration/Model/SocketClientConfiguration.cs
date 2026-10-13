using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    /// <summary>
    /// The address of one TCP device. SFC.json and Pdca.json entries are this alone; CCDs,
    /// scanners and robots add their PLC binding (<see cref="NetworkDeviceConfiguration"/>).
    /// A file may hold the port as a string; it is read as a number.
    /// </summary>
    public class SocketClientConfiguration
    {
        public int Id { get; set; }
        public string EnumName { get; set; }
        public string Name { get; set; }
        public string IPAddress { get; set; }
        public int Port { get; set; }
    }
}
