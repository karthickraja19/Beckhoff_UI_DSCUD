using System;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Point-in-time read-back of one pneumatic cylinder: the aggregate extend/
    /// retract/interlock state plus per-sensor reed states (in the same order as the
    /// cylinder's configured sense addresses), for the Manual state board.
    /// </summary>
    public class CylinderStatusSnapshot
    {
        public string ComponentId { get; set; }
        public bool IsExtended { get; set; }
        public bool IsRetracted { get; set; }
        public bool IsBothSensed { get; set; }
        public bool SafeToExtend { get; set; }
        public bool SafeToRetract { get; set; }

        /// <summary>Live state of each extend-side reed, ordered as configured.</summary>
        public bool[] ExtendSenseStates { get; set; } = Array.Empty<bool>();

        /// <summary>Live state of each retract-side reed, ordered as configured.</summary>
        public bool[] RetractSenseStates { get; set; } = Array.Empty<bool>();
    }
}
