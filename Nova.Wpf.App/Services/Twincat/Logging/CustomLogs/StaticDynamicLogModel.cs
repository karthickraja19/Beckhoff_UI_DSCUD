using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// One row of the Static / Dynamic log: one vision command of a run. The PLC declares it as
    /// <c>ST_SDLog</c> (Calibration\StaticDynamic\ST_SDLog.TcDUT) and FB_SDWork.WriteLog logs it
    /// with <c>MAIN.sdlog.LogRow(ADR(st), SIZEOF(st))</c>.
    ///
    /// The members are the PLC struct's, in the same order; their names are the CSV columns
    /// (the PLC's type prefix left off). Layout rules: see <see cref="ProductionLogModel"/>. The
    /// PLC struct has no pack attribute, so each member is aligned to its own size (Pack = 8).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct StaticDynamicLogModel
    {
        /// <summary>'Static' / 'Dynamic'.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)] public string Mode;

        /// <summary>1 / 2.</summary>
        public short Ccd;

        /// <summary>The vision command number, 1 .. Iteration.</summary>
        public short Shot;

        /// <summary>Commands wanted.</summary>
        public short Iteration;

        /// <summary>The recipe row of the working position.</summary>
        public short RecipeRow;

        /// <summary>The recipe position the axes moved to, mm.</summary>
        public double X;
        public double Y;
        public double Z;
        public double A;
        public double C;

        /// <summary>X / Y / Z received from the vision.</summary>
        public double VisionX;
        public double VisionY;
        public double VisionZ;

        /// <summary>The whole vision answer.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 251)] public string Response;

        /// <summary>'OK' / 'COMMAND NOT RECEIVED'.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 25)] public string Result;
    }
}
