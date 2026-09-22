namespace Nova.ViewModels.Common
{
    /// <summary>
    /// Temporary commissioning switch: while set, the Config and Settings pages behave
    /// as if a Level 3 operator were signed in, whether anyone is signed in or not.
    /// </summary>
    /// <remarks>
    /// Set once by the host at startup from the <c>BypassAccessLevels</c> app setting,
    /// before any view model is built - Nova.ViewModels is netstandard and cannot reach
    /// ConfigurationManager itself. Home, Alarm, Data and Vision keep their normal
    /// rules. Edits made under the bypass are logged against whoever is signed in,
    /// which may be nobody. Turn it off before the machine ships.
    /// </remarks>
    public static class AccessBypass
    {
        public static bool ConfigAndSettings { get; set; }
    }
}
