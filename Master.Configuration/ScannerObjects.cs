using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Teal.Library.Contracts;
using Teal.Library.Log;
using Teal.Library.TcpIp;

namespace Master.Configuration
{
    public static class ScannerObjects
    {
        public static Dictionary<Scanners, Scanner> Scanners = new Dictionary<Scanners, Scanner>();

        public static void LoadScanners()
        {
            try
            {
                var scannerJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\Scanners.json");

                ConfigGlobalClass.ScannerConfigurations = JsonConvert.DeserializeObject<List<SocketClientConfiguration>>(scannerJsonString);

                foreach (var scannerEnum in Enum.GetValues(typeof(Scanners)).Cast<Scanners>())
                {
                    string ccdName = scannerEnum.ToString();

                    var scannerConfig = ConfigGlobalClass.ScannerConfigurations.Where(x => x.EnumName == ccdName).FirstOrDefault();
                    ScannerProperties scannerProperties = new ScannerProperties();
                    var scanner = new Scanner(ConfigGlobalClass.cardManager.Card, scannerProperties);

                    Scanners[scannerEnum] = scanner;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("LoadScanners: " + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.WriteLog(LogType.Error, "LoadScanners", "CCDConfig", ex.Message, ex.StackTrace);
            }
        }
    }
}
