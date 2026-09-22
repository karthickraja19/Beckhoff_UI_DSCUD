using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Teal.Library.Contracts;
using Teal.Library.Log;
using Teal.Library.TcpIp;

namespace Master.Configuration
{
    public static class ScannerObjects
    {
        public static Dictionary<Scanners, TcpDevice> Scanners = new Dictionary<Scanners, TcpDevice>();

        public static void LoadScanners()
        {
            try
            {
                var scannerJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\Scanners.json");

                ConfigGlobalClass.ScannerConfigurations = JsonConvert.DeserializeObject<List<SocketClientConfiguration>>(scannerJsonString);

                foreach (var scannerEnum in Enum.GetValues(typeof(Scanners)).Cast<Scanners>())
                {
                    string scannerName = scannerEnum.ToString();

                    var scannerConfig = ConfigGlobalClass.ScannerConfigurations.Where(x => x.EnumName == scannerName).FirstOrDefault();
                    NetworkDeviceProperties networkDeviceProperties = new NetworkDeviceProperties
                    {
                        Name = scannerConfig.Name,
                        EnumName = scannerConfig.EnumName,
                        DeviceType = NetworkDeviceType.Scanner,
                        Id = scannerConfig.Id,
                    };
                    var scanner = TcpDeviceFactory.Create(ConfigGlobalClass.cardManager.Card,networkDeviceProperties);
                    Scanners[scannerEnum] = scanner;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("LoadScanners: " + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.WriteLog(LogType.Error, "LoadScanners", "ScannerObjects", ex.Message, ex.StackTrace);
            }
        }
    }
}
