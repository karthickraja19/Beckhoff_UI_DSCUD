using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// A PLC sequence's last log entry. Every sequence object in the PLC (LoadingConveyor,
    /// UnloadingConveyor, ...) has one and writes it each time it logs. The PLC declares it as
    /// <code>
    /// TYPE ST_Sequence :
    /// STRUCT
    ///     LogType      : Modules;
    ///     StationName  : STRING;
    ///     SequenceName : STRING;
    ///     StepNumber   : UINT;
    ///     Message      : STRING;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// A plain STRING is STRING(80): 80 characters and a closing zero byte. Modules is an
    /// enum with no base type, so an INT. 248 bytes, laid out as TwinCAT does (each field
    /// aligned to its own size). If the PLC struct changes, change this to match - the
    /// subscription compares the sizes and reports a mismatch.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct ST_Sequence
    {
        public short LogType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string StationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string SequenceName;
        public ushort StepNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string Message;

        /// <summary>True until the sequence has logged something.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Message) && string.IsNullOrEmpty(StationName) && StepNumber == 0;

        public bool SameAs(ST_Sequence other)
        {
            return LogType == other.LogType
                && StepNumber == other.StepNumber
                && StationName == other.StationName
                && SequenceName == other.SequenceName
                && Message == other.Message;
        }
    }
}
