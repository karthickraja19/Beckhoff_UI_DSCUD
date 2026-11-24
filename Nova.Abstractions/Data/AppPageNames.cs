namespace Nova.Abstractions.Data
{
    /// <summary>
    /// Routing key for every navigable page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first block is the six tabs Audio SW DFM v5.4 slide 42 puts in the
    /// indicator bar, in the order they appear there. Those six are the only pages
    /// the navigation service ever routes to - this HMI drives one Audio machine and
    /// its GUI is the DFM's.
    /// </para>
    /// <para>
    /// The second block is not routable. Those view models are hosted inside a tab of
    /// one of the six - Manual in Setting's Manual tab (the "GUI for manual operation"
    /// slide 57 gives Home's Vendor tab), MachineConfig, Recipe and Calibration in Config's Machine Configs area
    /// (slide 58) - but they still derive from PageViewModel, which requires a key.
    /// They are listed here so each has a distinct identity; nothing navigates to
    /// them, so no page factory case exists for them.
    /// </para>
    /// </remarks>
    public enum AppPageNames
    {
        // Indicator bar, left to right (DFM slide 42).
        Home,
        Alarm,
        Config,
        Data,
        Vision,
        Settings,

        // Hosted inside the pages above; never routed to directly.
        Manual,
        MachineConfig,
        Recipe,
        Calibration
    }
}
