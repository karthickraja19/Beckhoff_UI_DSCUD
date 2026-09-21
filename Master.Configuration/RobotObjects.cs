using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Teal.Library.Log;

namespace Master.Configuration
{
    public static class RobotObjects
    {
        //public static Dictionary<Robots, Dictionary<string, SocketClient>> Robots = 
        //    new Dictionary<Robots, Dictionary<string, SocketClient>>();

        public static void LoadRobots()
        {
            try
            {
                var robotsJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\Robots.json");

                ConfigGlobalClass.RobotConfigurations = JsonConvert.DeserializeObject<List<RobotConfigurations>>(robotsJsonString);

                foreach (var scannerEnum in Enum.GetValues(typeof(Robots)).Cast<Robots>())
                {
                    
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
