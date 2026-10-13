using System.Runtime.InteropServices;
using Nova.Abstractions.Hardware;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// An I/O stepper's status in FB_StepperSD (stepperdata[Id].status), the struct the library
    /// gives as its HMI display on the Stepper tab. Named as in the PLC so App.config's
    /// IOStepperHmiDisplayType finds it. The PLC declares it as
    /// <code>
    /// TYPE ST_STEPPER_STATUS :
    /// STRUCT
    ///     bpoweron, bready, benable, brunning, bbusy, bdone, bhomed, berror : BOOL;
    ///     nerrorid : UDINT;
    ///     salarmdiscription, serrordiscription : STRING(100);
    ///     bpositive_limi_sensor, bnegative_limit_sensor, bhome_Sensor : BOOL;
    ///     factualvelocity, factualposition : LREAL;
    ///     bfwd, brev : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// Laid out as TwinCAT does: the BOOLs at 0-7, nerrorid at 8, the strings at 12 and 113,
    /// the sensors at 214-216, the LREALs at 224 and 232, bfwd/brev at 240 - 248 bytes.
    /// </summary>
    /// <remarks>
    /// For an I/O stepper the PLC sets bbusy (its run latch, not brunning), berror and bfwd/brev
    /// (its direction outputs); the HMI has no on or off for it, so benable is not shown, and the
    /// homing, limit, position and velocity members are for a stepper run as an NC axis, which
    /// this HMI drives as a servo.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct ST_STEPPER_STATUS : IAxisHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)] public bool bpoweron;
        [MarshalAs(UnmanagedType.I1)] public bool bready;
        [MarshalAs(UnmanagedType.I1)] public bool benable;
        [MarshalAs(UnmanagedType.I1)] public bool brunning;
        [MarshalAs(UnmanagedType.I1)] public bool bbusy;
        [MarshalAs(UnmanagedType.I1)] public bool bdone;
        [MarshalAs(UnmanagedType.I1)] public bool bhomed;
        [MarshalAs(UnmanagedType.I1)] public bool berror;
        public uint nerrorid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 101)] public string salarmdiscription;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 101)] public string serrordiscription;
        [MarshalAs(UnmanagedType.I1)] public bool bpositive_limi_sensor;
        [MarshalAs(UnmanagedType.I1)] public bool bnegative_limit_sensor;
        [MarshalAs(UnmanagedType.I1)] public bool bhome_Sensor;
        public double factualvelocity;
        public double factualposition;
        [MarshalAs(UnmanagedType.I1)] public bool bfwd;
        [MarshalAs(UnmanagedType.I1)] public bool brev;

        public void Fill(AxisStatusSnapshot snapshot)
        {
            bool moving = bbusy || brunning;

            snapshot.IsInError = berror;
            snapshot.IsMoving = moving;
            snapshot.DirectionForward = bfwd;
            snapshot.DirectionReverse = brev;
            snapshot.State = berror ? "Error" : (moving ? "Running" : "Ready");
        }
    }
}
