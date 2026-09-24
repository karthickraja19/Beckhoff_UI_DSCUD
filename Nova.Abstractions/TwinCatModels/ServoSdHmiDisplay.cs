using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nova.ViewModels.TwinCatModels
{

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ServoSdHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool AxisEnable;

        [MarshalAs(UnmanagedType.I1)]
        public bool AxisError;

        [MarshalAs(UnmanagedType.I1)]
        public bool NegLimit;

        [MarshalAs(UnmanagedType.I1)]
        public bool PosLimit;

        [MarshalAs(UnmanagedType.I1)]
        public bool AxisHomed;

        [MarshalAs(UnmanagedType.I1)]
        public bool AxisHome;

        [MarshalAs(UnmanagedType.I1)]
        public bool AxisRunning;

        public double CurrentPosition;
        public double CurrentVelocity;
    }
}
