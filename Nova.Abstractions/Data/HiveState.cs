namespace Nova.Abstractions.Data
{
    /// <summary>
    /// The machine state shown on the HIVE status chip in the indicator bar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Audio SW DFM v5.4 slide 7 defines three HIVE states and numbers them: Running
    /// is 1, Idle is 2, Downtime is 5. Those numbers are the values reported in the
    /// <c>machine_state</c> payload, so they are pinned here explicitly rather than
    /// left to declaration order.
    /// </para>
    /// <para>
    /// <see cref="Disconnected"/> is not a machine state - it is the absence of one,
    /// and the DFM gives it its own chip ("HIVE is disabled or disconnected", slide
    /// 39). Planned downtime is state 5 with a "PD-" code (slide 9); it has no chip of
    /// its own - the 3 state standard (v5.0) shows it as Downtime.
    /// </para>
    /// <para>
    /// This enum is display state only. Nothing in this build reports it to HIVE or
    /// reads it back - the reporting side is deliberately out of scope for now.
    /// </para>
    /// </remarks>
    public enum HiveState
    {
        /// <summary>No HIVE link. Grey "HIVE" chip.</summary>
        Disconnected = 0,

        /// <summary>State 1. Machine is processing POR units.</summary>
        Running = 1,

        /// <summary>State 2. Machine can process POR units but currently is not.</summary>
        Idle = 2,

        /// <summary>State 5, planned or unplanned. Machine is unable to process POR units.</summary>
        Downtime = 5
    }
}
