namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Point-in-time read-back of one vacuum unit: the feedback-input state (vacuum
    /// sensed) plus the last commanded mode, for the Manual vacuum tab.
    /// </summary>
    public class VacuumStatusSnapshot
    {
        public string ComponentId { get; set; }

        /// <summary>Vacuum feedback input currently active (part sensed / vacuum achieved).</summary>
        public bool FeedbackOn { get; set; }

        /// <summary>Vacuum on/off output currently commanded.</summary>
        public bool VacuumCommanded { get; set; }

        /// <summary>Blow output currently commanded.</summary>
        public bool BlowCommanded { get; set; }
    }
}
