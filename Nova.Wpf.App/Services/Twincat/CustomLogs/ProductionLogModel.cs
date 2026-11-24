using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// SAMPLE custom log model: one row of the production log. The PLC declares it as
    /// <code>
    /// TYPE ST_ProductionLog :
    /// STRUCT
    ///     sProductCode      : STRING(30);
    ///     bProductionStatus : BOOL;
    ///     rCycleTime        : LREAL;
    ///     nIteration        : DINT;
    ///     bTossing          : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// and logs it with <c>MAIN.prodlog.LogRow(ADR(st), SIZEOF(st))</c>.
    ///
    /// The members must be the same as the PLC struct's, in the same order. Their names are the
    /// CSV column names. Layout rules, as for <see cref="ST_SeqLog"/>: STRING(n) is
    /// <c>ByValTStr</c> of n + 1; BOOL is <c>UnmanagedType.U1</c> (a plain C# bool is 4 bytes
    /// in a struct); INT / DINT / LINT are short / int / long; REAL / LREAL are float / double;
    /// TIME is a uint (ms) and DT a uint (s). Flat only. Declare no public properties: with
    /// any, the log would use those instead of the fields. If the sizes differ the bridge
    /// refuses the row and says so in the log.
    ///
    /// From C# the same log works too: <c>CustomLogs.Production.Log(model)</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct ProductionLogModel
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string ProductCode;

        [MarshalAs(UnmanagedType.U1)] public bool ProductionStatus;

        public double CycleTime;

        public int Iteration;

        [MarshalAs(UnmanagedType.U1)] public bool Tossing;
    }
}
