namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Identity of the machine this install is driving, read once from the machine
    /// profile at startup. Separate from <see cref="IMachineSettingsService"/>: those are
    /// operator-editable settings, these are fixed facts about the installation that the
    /// title bar and the help dialog report so an operator on the floor can say which
    /// machine and which build they are looking at.
    /// </summary>
    public interface IMachineIdentityService
    {
        /// <summary>
        /// Machine name from the profile (for example "AAA"). Falls back to "Unknown"
        /// rather than empty when the profile is missing or unreadable — a blank title
        /// bar tells the operator nothing, and this must never be a reason not to start.
        /// </summary>
        string MachineName { get; }

        /// <summary>Application/software version from the profile.</summary>
        string SoftwareVersion { get; }

        /// <summary>
        /// Machine hardware variant from the profile (for example "V57"). Distinct from
        /// <see cref="SoftwareVersion"/> — two machines on the same build can be
        /// different mechanical revisions.
        /// </summary>
        string MachineVersion { get; }

        /// <summary>
        /// Ready-to-display version string for the title bar, combining the two above.
        /// </summary>
        string DisplayVersion { get; }
    }
}
