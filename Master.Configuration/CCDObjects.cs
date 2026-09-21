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
    public static class CCDObjects
    {
        public static Dictionary<CCDs, Vision> CCDs = new Dictionary<CCDs, Vision>();

        public static void LoadCCDs()
        {
            try
            {
                var ccdJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\CCDs.json");

                ConfigGlobalClass.CCDConfigurations = JsonConvert.DeserializeObject<List<SocketClientConfiguration>>(ccdJsonString);

                foreach (var ccdEnum in Enum.GetValues(typeof(CCDs)).Cast<CCDs>())
                {
                    string ccdName = ccdEnum.ToString();

                    var ccdConfig = ConfigGlobalClass.CCDConfigurations.Where(x => x.EnumName == ccdName).FirstOrDefault();
                    VisionProperties visionProperties = new VisionProperties();
                    var ccd = new Vision(ConfigGlobalClass.cardManager.Card, visionProperties);

                    CCDs[ccdEnum] = ccd;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("LoadCCDs: " + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.WriteLog(LogType.Error, "LoadCCDs", "CCDConfig", ex.Message, ex.StackTrace);
                //throw;
            }
        }
    }
}
