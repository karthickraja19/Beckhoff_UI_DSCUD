namespace Nova.Abstractions.Data
{
    /// <summary>
    /// Operator access level, per Audio SW DFM v5.4 slide 39 "User Level" and slide 44.
    /// </summary>
    /// <remarks>
    /// The level is what every enable/read-only rule in the GUI is decided from, so it
    /// is a first-class value rather than a string compared against literals:
    /// <list type="bullet">
    /// <item><description><see cref="None"/> - Home, Alarm, Data and Vision are
    /// enabled but read-only; Config and Setting are disabled (slide 42).</description></item>
    /// <item><description><see cref="Level1"/> - "User ID is logged. Normal production
    /// access. All parameters are read-only and cannot be changed."</description></item>
    /// <item><description><see cref="Level2"/> - "Engineer ID is logged. Universal
    /// change access. Key parameters cannot be changed."</description></item>
    /// <item><description><see cref="Level3"/> - "Admin ID is logged. Can change all
    /// parameters. All actions must be recorded by log."</description></item>
    /// </list>
    /// Ordered so <c>&gt;=</c> means "at least this level", which is how the gating
    /// reads in the view models.
    /// </remarks>
    public enum AccessLevel
    {
        None = 0,
        Level1 = 1,
        Level2 = 2,
        Level3 = 3
    }
}
