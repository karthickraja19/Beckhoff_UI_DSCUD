using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.TcpIp;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The library vision device for each member of the <see cref="Master.Configuration.CCDs"/>
    /// enum configured in Data\CCDs.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class CCDObjects
    {
        public static readonly Dictionary<CCDs, TcpDevice> CCDs = new Dictionary<CCDs, TcpDevice>();

        /// <summary>
        /// Each is bound to {DBName}.{VisionDUTName}.VisionData[Id] from CardsConfiguration.json
        /// (MAIN.visioncall.VisionData[1]). Without a VisionDUTName no CCD is created. The PLC array starts at 1.
        /// </summary>
        internal static void Load(TwincatCard card, List<string> problems)
        {
            CCDs.Clear();

            foreach (var ccd in MachineConfiguration.Configured<Master.Configuration.CCDs, VisionConfiguration>(ConfigGlobalClass.CCDConfigurations, c => c.EnumName))
            {
                var properties = DeviceObjects.NetworkProperties<VisionProperties>(ccd.Value, card, ConfigGlobalClass.CardConfiguration?.VisionDUTName);
                if (ccd.Value.TriggerCommand != null)
                    properties.TriggerCommand = ccd.Value.TriggerCommand;

                DeviceObjects.Add(CCDs, ccd.Key, () => TcpDeviceFactory.Create(card, properties), problems);
            }
        }
    }
}
