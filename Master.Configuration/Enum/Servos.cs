using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration
{
    #region List of all Servos
    /// <summary>List of all Servos, in the order of Data\Servos.json (each one's Id is its PLC array index).</summary>
    public enum Servos
    {
        UC_Loading_X_Axis_Gantry,
        UC_Loading_Z_Axis_Gantry_Servo,
        Station_1_Vision_Gantry_X_Axis,
        Station_1_Dispensing_Gantry_X_Axis,
        Station_1_Vision_Gantry_Z_Axis,
        Station_1_Dispensing_Gantry_Z_Axis,
        Station_1_Working_Module_Y_Axis,
        Station_1_Working_Module_A_Axis,
        Station_1_Working_Module_R_Axis,
        Station_2_Vision_Gantry_X_Axis,
        Station_2_Dispensing_Gantry_X_Axis,
        Station_2_Vision_Gantry_Z_Axis,
        Station_2_Dispensing_Gantry_Z_Axis,
        Station_2_Working_Module_Y_Axis,
        Station_2_Working_Module_A_Axis,
        Station_2_Working_Module_R_Axis,
        UC_Unloading_X_Axis_Gantry,
        UC_Unloading_Z_Axis_Gantry,
        Automatic_Weighing_Module,
    }
    #endregion
}
