using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.TcpIp;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The library scanner for each member of the <see cref="Master.Configuration.Scanners"/>
    /// enum configured in Data\Scanners.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class ScannerObjects
    {
        public static readonly Dictionary<Scanners, TcpDevice> Scanners = new Dictionary<Scanners, TcpDevice>();

        /// <summary>
        /// Each is bound to {DBName}.{ScannerDUTName}.ScannerData[Id] from CardsConfiguration.json
        /// (MAIN.scannercall.ScannerData[1]). Without a ScannerDUTName no scanner is created. The PLC array starts at 1.
        /// </summary>
        internal static void Load(TwincatCard card, List<string> problems)
        {
            Scanners.Clear();

            foreach (var scanner in MachineConfiguration.Configured<Master.Configuration.Scanners, NetworkDeviceConfiguration>(ConfigGlobalClass.ScannerConfigurations, c => c.EnumName))
            {
                var properties = DeviceObjects.NetworkProperties<ScannerProperties>(scanner.Value, card, ConfigGlobalClass.CardConfiguration?.ScannerDUTName);
                DeviceObjects.Add(Scanners, scanner.Key, () => TcpDeviceFactory.Create(card, properties), problems);
            }
        }
    }
}
