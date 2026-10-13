using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration.Model
{
    /// <summary>
    /// CardsConfiguration.json: the library's CardProperties, property for property, plus the
    /// arrays of the TCP devices. The robots are on EtherCAT and need nothing here. {DBName}.{ServoDUTName} (and CylinderDUTName, VacuumDUTName)
    /// is the PLC's mapping block for that kind of device, for example MAIN.servocall; the
    /// library finds each device in it by Id - MAIN.servocall.servoItf[Id],
    /// MAIN.cylindercall.CylinderData[Id], MAIN.vaccumcall.vacuumdata[Id].
    /// </summary>
    public class CardConfiguration
    {
        public string ConnectionId { get; set; }
        public string DBName { get; set; }
        public int PortNo { get; set; }
        public string ServoDUTName { get; set; }
        public string StepperDUTName { get; set; }
        public string VacuumDUTName { get; set; }
        public string CylinderDUTName { get; set; }
        public int InputCount { get; set; }
        public int OutputCount { get; set; }

        /// <summary>
        /// The CCD and scanner arrays under DBName. The library takes them as each device's
        /// InstancePath ({DBName}.{VisionDUTName}); empty leaves the library's own, such as
        /// VisionGVL.Visions.
        /// </summary>
        public string VisionDUTName { get; set; }
        public string ScannerDUTName { get; set; }
    }
}
