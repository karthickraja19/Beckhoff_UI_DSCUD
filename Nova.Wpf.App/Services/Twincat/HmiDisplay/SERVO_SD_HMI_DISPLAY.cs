using System;
using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// The PLC's SERVO_SD_HMI_DISPLAY (FB_ServoSD.HMI_DISPLAY), field for field. Named as in
    /// the PLC so App.config's ServoHmiDisplayType finds it.
    /// </summary>
    /// <remarks>
    /// TwinCAT aligns each field to its own size, as <see cref="LayoutKind.Sequential"/> does
    /// here: the 7 BOOLs take bytes 0-6, one byte of padding, then the LREALs at 8 and 16 -
    /// 24 bytes. A change to the PLC struct must be made here too; a size that no longer
    /// matches is reported at startup and the axes show no status.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct SERVO_SD_HMI_DISPLAY : IAxisHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)] public bool bAXIS_ENABLE;
        [MarshalAs(UnmanagedType.I1)] public bool bAXIS_ERROR;
        [MarshalAs(UnmanagedType.I1)] public bool bNEG_LMT;
        [MarshalAs(UnmanagedType.I1)] public bool bPOS_LMT;
        [MarshalAs(UnmanagedType.I1)] public bool bAXIS_HOMED;
        [MarshalAs(UnmanagedType.I1)] public bool bAXIS_HOME;
        [MarshalAs(UnmanagedType.I1)] public bool bAXIS_RUNNING;
        public double lAXIS_CUR_POS;
        public double lAXIS_CUR_VELOCITY;

        public void Fill(AxisStatusSnapshot snapshot)
        {
            snapshot.ActualPosition = lAXIS_CUR_POS;
            snapshot.ActualSpeed = Math.Abs(lAXIS_CUR_VELOCITY);
            snapshot.IsEnabled = bAXIS_ENABLE;
            snapshot.IsInError = bAXIS_ERROR;
            snapshot.IsHomed = bAXIS_HOMED;
            snapshot.IsMoving = bAXIS_RUNNING;
            snapshot.PositiveLimit = bPOS_LMT;
            snapshot.NegativeLimit = bNEG_LMT;
            snapshot.AtHomeSensor = bAXIS_HOME;
            snapshot.DirectionForward = lAXIS_CUR_VELOCITY > 0;
            snapshot.DirectionReverse = lAXIS_CUR_VELOCITY < 0;
            snapshot.State = bAXIS_ERROR ? "Error" : (bAXIS_RUNNING ? "Running" : "Ready");
        }
    }
}
