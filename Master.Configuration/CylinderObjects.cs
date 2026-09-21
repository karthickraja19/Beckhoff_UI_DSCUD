using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Teal.Library.Contracts;
using Teal.Library.Cylinder;
using Teal.Library.Log;

namespace Master.Configuration
{
    public static class CylinderObjects
    {
        public static Dictionary<Cylinders, ICylinder> CylinderObjs = new Dictionary<Cylinders, ICylinder>();

        public static void LoadCylinders()
        {
            try
            {
                var cylinderJsonString = File.ReadAllText(Application.StartupPath + "\\Data\\Cylinders.json");

                ConfigGlobalClass.CylinderConfigurations = JsonConvert.DeserializeObject<List<CylinderConfiguration>>(cylinderJsonString);

                foreach (var cylinderEnum in Enum.GetValues(typeof(Cylinders)).Cast<Cylinders>())
                {
                    string cylinderName = cylinderEnum.ToString();

                    var cylinderConfig = ConfigGlobalClass.CylinderConfigurations.Where(x => x.EnumName == cylinderName).FirstOrDefault();

                    var props = new CylinderProperties
                    {
                        Name = cylinderConfig.Name,
                        ExtendSenses = cylinderConfig.ExtendSenses,
                        RetractSenses = cylinderConfig.RetractSenses,
                        ExtendOutput = cylinderConfig.ExtendValves,
                        RetractOutput = cylinderConfig.RetractValves,
                    };
                    
                    ICylinder cylinder = new Cylinder(ConfigGlobalClass.cardManager.Card,props);

                    CylinderObjs[cylinderEnum] = cylinder;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cylinder_Config_Error: " + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.WriteLog(LogType.Error, "Cylinder_Config", "CylinderConfig", ex.Message, ex.StackTrace);
                //throw;
            }
        }
    }
}
