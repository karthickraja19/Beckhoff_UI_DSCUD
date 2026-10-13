using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A vacuum's HMI display: what the vacuum board shows. Named as the PLC struct must be so
    /// App.config's VacuumHmiDisplayType finds it. The PLC declares it as
    /// <code>
    /// TYPE VACUUM_SD_HMI_DISPLAY :
    /// STRUCT
    ///     bVACUUM_OK : BOOL;
    ///     bVACUUM_ON : BOOL;
    ///     bBLOW_ON : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// 3 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VACUUM_SD_HMI_DISPLAY : IVacuumHmiDisplay
    {
        /// <summary>The vacuum sensor sees vacuum.</summary>
        [MarshalAs(UnmanagedType.I1)] public bool bVACUUM_OK;

        /// <summary>The vacuum valve is on.</summary>
        [MarshalAs(UnmanagedType.I1)] public bool bVACUUM_ON;

        /// <summary>The blow valve is on.</summary>
        [MarshalAs(UnmanagedType.I1)] public bool bBLOW_ON;

        public void Fill(VacuumStatusSnapshot snapshot)
        {
            snapshot.FeedbackOn = bVACUUM_OK;
            snapshot.VacuumCommanded = bVACUUM_ON;
            snapshot.BlowCommanded = bBLOW_ON;
        }
    }
}
