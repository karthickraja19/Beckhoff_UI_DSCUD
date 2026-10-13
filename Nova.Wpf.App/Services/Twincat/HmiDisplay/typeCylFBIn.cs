using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A cylinder's inputs in FB_CylinderSD (typeCylFB.in), the struct the library gives as
    /// the cylinder's HMI display while the PLC has no CYLINDER_SD_HMI_DISPLAY: its sensors
    /// are what the cylinder board shows. Work = extended, home = retracted. The PLC declares it as
    /// <code>
    /// TYPE typeCylFBIn :
    /// STRUCT
    ///     blockEnable : BOOL;
    ///     operationEnable : BOOL;
    ///     sensHomeFB : BOOL;
    ///     sensWorkFB : BOOL;
    ///     checkHomeSafe : BOOL;
    ///     checkWorkSafe : BOOL;
    ///     reset : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// 7 bytes. FB_CylinderSD has one sensor per side, so the board's sensor chips are one each.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct typeCylFBIn : ICylinderHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)] public bool blockEnable;
        [MarshalAs(UnmanagedType.I1)] public bool operationEnable;
        [MarshalAs(UnmanagedType.I1)] public bool sensHomeFB;
        [MarshalAs(UnmanagedType.I1)] public bool sensWorkFB;
        [MarshalAs(UnmanagedType.I1)] public bool checkHomeSafe;
        [MarshalAs(UnmanagedType.I1)] public bool checkWorkSafe;
        [MarshalAs(UnmanagedType.I1)] public bool reset;

        public void Fill(CylinderStatusSnapshot snapshot)
        {
            snapshot.ExtendSenseStates = new[] { sensWorkFB };
            snapshot.RetractSenseStates = new[] { sensHomeFB };
            snapshot.IsExtended = sensWorkFB && !sensHomeFB;
            snapshot.IsRetracted = sensHomeFB && !sensWorkFB;
            snapshot.IsBothSensed = sensWorkFB && sensHomeFB;
        }
    }
}
