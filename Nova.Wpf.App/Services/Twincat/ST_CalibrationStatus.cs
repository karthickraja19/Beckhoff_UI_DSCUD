using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The PLC's calibration status (App.config PlcCalibrationStatusSymbol). The PLC declares it as
    /// <code>
    /// TYPE ST_CalibrationStatus :
    /// STRUCT
    ///     eState    : E_CalibState;              // INT: 0 Idle, 1 Running, 2 Paused, 3 WaitOperator, 4 Done, 5 Failed, 6 Stopped
    ///     nStation  : INT;                       // 1 or 2: the station being (or last) calibrated
    ///     nRoutine  : INT;                       // E_CalibRoutine being (or last) run
    ///     nStep     : INT;                       // the sequence's step
    ///     sStep     : STRING;                    // what the step does, for the screen
    ///     sPrompt   : STRING;                    // what the operator must check in WaitOperator
    ///     bResultOk : BOOL;                      // Done: every check passed
    ///     aResult   : ARRAY[1..80] OF LREAL;     // measured values, row by row as the routine lays them out
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// E_CalibState has no base type, so it is an INT. A plain STRING is STRING(80): 80
    /// characters and a closing zero byte. 816 bytes, laid out as TwinCAT does (each field
    /// aligned to its own size). If the PLC struct changes, change this to match - the
    /// subscription compares the sizes and reports a mismatch.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct ST_CalibrationStatus
    {
        public const int ResultCount = 80;

        public short eState;
        public short nStation;
        public short nRoutine;
        public short nStep;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string sStep;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string sPrompt;
        [MarshalAs(UnmanagedType.I1)] public bool bResultOk;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ResultCount)] public double[] aResult;
    }
}
