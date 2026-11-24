using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// What one PLC module's sequence last reported. The PLC keeps one per module
    /// (FB_SeqLogMapping.aLog; symbol map: SequenceLog.Entries) and declares it as
    /// <code>
    /// TYPE ST_SeqLog :
    /// STRUCT
    ///     nModule      : INT;          // GVL_Module number
    ///     sStationName : STRING(30);   // readable module name
    ///     sSeqName     : STRING(30);   // which sequence wrote this
    ///     nStepNo      : INT;          // its step number
    ///     sMessage     : STRING(80);   // what it is doing
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// A STRING(n) is n characters and a closing zero byte. 148 bytes, laid out as TwinCAT
    /// does (each field aligned to its own size). If the PLC struct changes, change this to
    /// match - the subscription compares the sizes and reports a mismatch.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct ST_SeqLog
    {
        public short Module;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string StationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string SequenceName;

        public short StepNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string Message;

        /// <summary>True until the sequence has logged something (the station name alone is set by the PLC at start).</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Message) && string.IsNullOrEmpty(SequenceName) && StepNumber == 0;

        public bool SameAs(ST_SeqLog other)
        {
            return Module == other.Module
                && StepNumber == other.StepNumber
                && StationName == other.StationName
                && SequenceName == other.SequenceName
                && Message == other.Message;
        }
    }
}
