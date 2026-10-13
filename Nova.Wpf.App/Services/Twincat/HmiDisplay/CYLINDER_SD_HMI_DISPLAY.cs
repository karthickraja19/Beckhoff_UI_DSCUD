using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A cylinder's HMI display: what the cylinder board shows, in FB_CylinderSD's terms (work
    /// = extended, home = retracted). Named as the PLC struct must be so App.config's
    /// CylinderHmiDisplayType finds it. The PLC declares it as
    /// <code>
    /// TYPE CYLINDER_SD_HMI_DISPLAY :
    /// STRUCT
    ///     bSENS_WORK : BOOL;
    ///     bSENS_HOME : BOOL;
    ///     bIN_WORK : BOOL;
    ///     bIN_HOME : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// 4 bytes. FB_CylinderSD has one sensor per side, so the board's sensor chips are one each.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct CYLINDER_SD_HMI_DISPLAY : ICylinderHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)] public bool bSENS_WORK;
        [MarshalAs(UnmanagedType.I1)] public bool bSENS_HOME;
        [MarshalAs(UnmanagedType.I1)] public bool bIN_WORK;
        [MarshalAs(UnmanagedType.I1)] public bool bIN_HOME;

        public void Fill(CylinderStatusSnapshot snapshot)
        {
            snapshot.ExtendSenseStates = new[] { bSENS_WORK };
            snapshot.RetractSenseStates = new[] { bSENS_HOME };
            snapshot.IsExtended = bIN_WORK;
            snapshot.IsRetracted = bIN_HOME;
            snapshot.IsBothSensed = bSENS_WORK && bSENS_HOME;
        }
    }
}
