using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Machine.Enums
{
    /// <summary>
    /// Enum Variable for the machine ErrorCode
    /// </summary>
    public enum ECode
    {
        Start = -1,
        Invalid_ECode,
        Cyl_Extend_Fail,
        Cyl_Retract_Fail,
        Cyl_Extend_Timeout,
        Cyl_Retract_Timeout,
        Cyl_UnSafe_Extend,
        Cyl_UnSafe_Retract,
        Cyl_Both_Sensor,
        // Named Cyl_Interlock_Fail to match Master.SharedUtilities.ECode and the
        // AlarmEnum column of Data\AlarmInfo.csv. Dropping the Cyl_ prefix here
        // silently unmapped this alarm from its code and description.
        Cyl_Interlock_Fail,
        PosLimit_Error,
        NegLimit_Error,
        Disable_Error,
        MoveTimeout_Error,
        DriveError_Error,
        Emg_Pressed_Error,
        SafetyDoor_open_Error,
        MainAir_FB_Error,
        Homing_TimeOut_Error,
        ProgramException_Error,
        Motion_Time_out_Error,
        Motion_Card_not_open,
        Robot_Alarm_Error,
        Robot_Receive_TimeOut_Error,
        Vac_VacuumOnOffFail,
        Vac_BlowOnOffFail,
        Vac_TimeoutEcode,
        IO_Stepper_ForwardFail,
        IO_Stepper_ReverseFail,
        IO_Stepper_StopFail,
        IO_Stepper_AlarmResetFail,
        TotalAlarm,
    }
}



