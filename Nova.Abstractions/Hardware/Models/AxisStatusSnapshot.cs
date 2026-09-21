namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Point-in-time read-back of a single axis: live position/speed plus the
    /// status flags the Manual screen displays. Platform-free replacement for the
    /// V3 pattern of calling GetCurrentPos + ToGetMotionIOStatus from a UI timer.
    /// </summary>
    public class AxisStatusSnapshot
    {
        public string ComponentId { get; set; }

        /// <summary>Actual position in user units (mm).</summary>
        public double ActualPosition { get; set; }

        /// <summary>Actual speed in user units per second (mm/s).</summary>
        public double ActualSpeed { get; set; }

        /// <summary>
        /// The axis parameter speed from Axis_Parameters.xml (Teal IAxis.Velocity) — the
        /// ceiling a manual move, jog or home may be commanded at. Zero when unknown,
        /// which callers treat as "no limit configured" rather than "no motion allowed".
        /// Surfaced on the snapshot because it is the value V3's Manual and Recipe
        /// screens validate every operator-entered speed against; without it the UI
        /// accepted any number and the drive quietly ran at its own configured speed.
        /// </summary>
        public double SpeedLimit { get; set; }

        public bool IsEnabled { get; set; }
        public bool IsInError { get; set; }
        public bool NegativeLimit { get; set; }
        public bool PositiveLimit { get; set; }

        /// <summary>Origin (home) sensor currently active.</summary>
        public bool AtHomeSensor { get; set; }

        /// <summary>Homing has completed since power-up.</summary>
        public bool IsHomed { get; set; }

        public bool IsMoving { get; set; }

        /// <summary>
        /// The drive's own state word as a string — what
        /// Teal.Library.Advantech.AdvantechAxis.GetAxisStatus() returns, e.g.
        /// "STA_AX_READY", "STA_AX_PTP_MOT", "STA_AX_ERROR_STOP". The booleans above are
        /// derived from it and answer "is it moving / faulted"; this answers "what
        /// exactly is it doing", which is what an engineer needs when an axis refuses a
        /// command for a reason none of the flags covers. Null when the drive does not
        /// report one.
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// Commanded/observed travel direction. Servos and axis-based steppers derive
        /// these from the signed velocity; an IO conveyor has no feedback at all, so its
        /// adapter latches whatever Forward/Reverse was last commanded. Both false when
        /// stopped.
        /// </summary>
        public bool DirectionForward { get; set; }

        public bool DirectionReverse { get; set; }
    }
}
