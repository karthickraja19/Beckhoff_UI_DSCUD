namespace Nova.Core.Config
{
    /// <summary>
    /// The software's own version, hard coded as Audio SW DFM v5.4 slide 43 requires
    /// ("SW Version ID is hard coded in SW and shows on station card").
    /// </summary>
    /// <remarks>
    /// Format <c>AAA_X.X.X.X_YYMMDD_TYPE</c>, slide 43:
    /// <list type="bullet">
    /// <item><description>AAA - vendor, three-letter abbreviation (TET, JST, BZ, ...).</description></item>
    /// <item><description>X.X.X.X - main, vision, laser and robot SW versions, in that order;
    /// 0 for a module the machine does not have.</description></item>
    /// <item><description>YYMMDD - SW release date.</description></item>
    /// <item><description>TYPE - POR (POR machine), BKP (backup machine), RTF (retrofit
    /// machine) or VAL (under validation).</description></item>
    /// </list>
    /// Update these constants for every release. The station card's MS_Hash changes
    /// with every build on its own; this string only changes when someone edits it.
    /// </remarks>
    public static class SoftwareRelease
    {
        public const string Vendor = "TET";

        public const int MainVersion = 1;
        public const int VisionVersion = 0;
        public const int LaserVersion = 0;
        public const int RobotVersion = 0;

        public const string ReleaseDate = "260921";

        /// <summary>VAL until the release is qualified; POR, BKP or RTF after.</summary>
        public const string Type = "VAL";

        public static string Version =>
            Vendor + "_" + MainVersion + "." + VisionVersion + "." + LaserVersion + "." + RobotVersion
            + "_" + ReleaseDate + "_" + Type;
    }
}
