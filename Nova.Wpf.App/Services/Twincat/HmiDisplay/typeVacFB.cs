using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A vacuum's whole data block in FB_VacuumMapping (vacuumdata[Id], typeVacFB), the struct
    /// the library gives as the vacuum's HMI display while the PLC has no VACUUM_SD_HMI_DISPLAY:
    /// the board needs the sensor (in) and what FB_VacuumSD made of the command (status), which
    /// are in different members of it. The PLC declares it as
    /// <code>
    /// TYPE typeVacFB :
    /// STRUCT
    ///     in     : typeVacFBIn;      // blockEnable, operationEnable, vacuumFeedbackSen, Safe_vacuumOff, Reset, vacuumCoil, blowCoil : BOOL
    ///     cmd    : typeVacFBCmd;     // vacuum, blow : BOOL
    ///     hmicmd : typeVacFBHmiCmd;  // vacuum, blow : BOOL
    ///     status : typeVacFBStatus;  // vacuum, blow, busy : BOOL
    ///     alarm  : typeVacFBAlarm;   // alarmActive, vacuumOnTimeout, VacuumFeedbackTimeOut, vacuumOffTimeout, BlowOnTimeout, BlowOffTimeout : BOOL
    ///     out    : typeVacFBOut;     // valveVacuum, valveBlow : BOOL
    ///     config : typeVacFBConfig;  // stationName, vacuumName : STRING(50); valveAddr, blowAddr : WORD; vacuumType, VacuumFBbypass : BOOL
    ///     par    : typeVacFBPar;     // vacuumTimeout, blowTimeout, vacuumStableCount, vacuumFeedbacTtimeout : TIME
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// Read here member for member, each named after its PLC struct member: 22 BOOLs (bytes
    /// 0-21), config from byte 22 (WORD aligned), par from byte 132 (TIME aligned) - 148 bytes.
    /// status.vacuum / status.blow are set once FB_VacuumSD has done the command (for vacuum, once
    /// the sensor is stable), whichever vacuumType wires the valves.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct typeVacFB : IVacuumHmiDisplay
    {
        // in
        [MarshalAs(UnmanagedType.I1)] public bool in_blockEnable;
        [MarshalAs(UnmanagedType.I1)] public bool in_operationEnable;
        [MarshalAs(UnmanagedType.I1)] public bool in_vacuumFeedbackSen;
        [MarshalAs(UnmanagedType.I1)] public bool in_Safe_vacuumOff;
        [MarshalAs(UnmanagedType.I1)] public bool in_Reset;
        [MarshalAs(UnmanagedType.I1)] public bool in_vacuumCoil;
        [MarshalAs(UnmanagedType.I1)] public bool in_blowCoil;

        // cmd
        [MarshalAs(UnmanagedType.I1)] public bool cmd_vacuum;
        [MarshalAs(UnmanagedType.I1)] public bool cmd_blow;

        // hmicmd
        [MarshalAs(UnmanagedType.I1)] public bool hmicmd_vacuum;
        [MarshalAs(UnmanagedType.I1)] public bool hmicmd_blow;

        // status
        [MarshalAs(UnmanagedType.I1)] public bool status_vacuum;
        [MarshalAs(UnmanagedType.I1)] public bool status_blow;
        [MarshalAs(UnmanagedType.I1)] public bool status_busy;

        // alarm
        [MarshalAs(UnmanagedType.I1)] public bool alarm_alarmActive;
        [MarshalAs(UnmanagedType.I1)] public bool alarm_vacuumOnTimeout;
        [MarshalAs(UnmanagedType.I1)] public bool alarm_VacuumFeedbackTimeOut;
        [MarshalAs(UnmanagedType.I1)] public bool alarm_vacuumOffTimeout;
        [MarshalAs(UnmanagedType.I1)] public bool alarm_BlowOnTimeout;
        [MarshalAs(UnmanagedType.I1)] public bool alarm_BlowOffTimeout;

        // out
        [MarshalAs(UnmanagedType.I1)] public bool out_valveVacuum;
        [MarshalAs(UnmanagedType.I1)] public bool out_valveBlow;

        // config
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 51)] public string config_stationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 51)] public string config_vacuumName;
        public ushort config_valveAddr;
        public ushort config_blowAddr;
        [MarshalAs(UnmanagedType.I1)] public bool config_vacuumType;
        [MarshalAs(UnmanagedType.I1)] public bool config_VacuumFBbypass;

        // par (TIME = milliseconds)
        public uint par_vacuumTimeout;
        public uint par_blowTimeout;
        public uint par_vacuumStableCount;
        public uint par_vacuumFeedbacTtimeout;

        public void Fill(VacuumStatusSnapshot snapshot)
        {
            snapshot.FeedbackOn = in_vacuumFeedbackSen;
            snapshot.VacuumCommanded = status_vacuum;
            snapshot.BlowCommanded = status_blow;
        }
    }
}
