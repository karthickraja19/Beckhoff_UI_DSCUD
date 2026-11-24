using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Teal.Library.Log;

namespace Master.Configuration
{
    public static class PdcaObject
    {
        //public static Dictionary<Pdca, SocketClient> PdcaObjs = new Dictionary<Pdca, SocketClient>(); 

        //public static SocketClient SFC = new SocketClient("SFC", 10);
        public static void LoadPdca()
        {
            try
            {
                if (!MachineConfiguration.IsLoaded)
                    MachineConfiguration.Load(Path.Combine(Application.StartupPath, "Data"));

                foreach (var PdcaEnum in Enum.GetValues(typeof(Pdca)).Cast<Pdca>())
                {
                    string pdcaName = PdcaEnum.ToString();

                    var pdcaConfig = ConfigGlobalClass.PdcaConfiguration.Where(x => x.EnumName == pdcaName).FirstOrDefault();

                    //var pdca = new SocketClient(pdcaConfig.IPAddress, pdcaConfig.Port, pdcaConfig.Name);


                    //PdcaObjs[PdcaEnum] = pdca;
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
