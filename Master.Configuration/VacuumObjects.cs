using Master.Configuration.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Teal.Library.Contracts;
using Teal.Library.Contracts.Enum;
using Teal.Library.Log;
using Teal.Library.Vacuum;

namespace Master.Configuration
{
    public static class VacuumObjects
    {
        public static Dictionary<Vacuums, IVacuum> VacuumObjs = new Dictionary<Vacuums, IVacuum>();

        public static void LoadVacuums()
        {
            try
            {
                var vacuumJsonString = File.ReadAllText(Application.StartupPath + "\\Data" + "\\Vacuums.json");

                ConfigGlobalClass.VacuumConfigurations = JsonConvert.DeserializeObject<List<VacuumConfiguration>>(vacuumJsonString);

                foreach (var vacuumEnum in Enum.GetValues(typeof(Vacuums)).Cast<Vacuums>())
                {
                    string vacuumName = vacuumEnum.ToString();

                    var vacuumConfig = ConfigGlobalClass.VacuumConfigurations.Where(x => x.EnumName == vacuumName).FirstOrDefault();

                    VacuumProperties vacuumProps = new VacuumProperties
                    {
                        Name = vacuumConfig.Name,
                        EnumName = vacuumConfig.EnumName,
                        VacuumOutput = vacuumConfig.VacuumOutput,
                        VacuumSense = vacuumConfig.VacuumSense,
                        BlowOutput = vacuumConfig.BlowOutput
                    };

                    var vacuum = new Vacuum(ConfigGlobalClass.cardManager.Card, vacuumProps);

                    VacuumObjs[vacuumEnum] = vacuum;
                }
            }

            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Vacuum_Config", "VacuumCOnfigConstr", ex.Message, ex.StackTrace);
                MessageBox.Show("Vacuum_Config_Error: " + ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
