namespace Nova.Abstractions.Data
{
    /// <summary>
    /// The four appearances an indicator button in the Audio/Home GUI can take, as
    /// named by Audio SW DFM v5.4 slide 39 "Indicator Legend".
    /// </summary>
    /// <remarks>
    /// Lives in Abstractions rather than beside the WPF control that draws it because
    /// the DFM's rules for which state applies are access-control and machine rules -
    /// "No login: Disabled / L1, 2, 3: Enabled", "Turn to Alarmed when an error
    /// occurs" - so a view model decides them and the view just draws what it is told.
    /// </remarks>
    public enum AudioIconState
    {
        /// <summary>The page is open, or the machine control is the one in effect.</summary>
        Energized,

        /// <summary>Pressable, but not the active one. Drawn grey (#CCCCCC).</summary>
        DeEnergized,

        /// <summary>Not pressable at the current access level. Drawn grey at half alpha.</summary>
        Disabled,

        /// <summary>Alarm button only: "Turn to Alarmed when an error occurs".</summary>
        Alerted
    }
}
