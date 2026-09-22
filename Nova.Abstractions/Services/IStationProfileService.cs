using System;
using System.Collections.Generic;
using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The "General" block of the Audio/Home Settings page (Audio SW DFM v5.4 slide 62)
    /// plus the facts the Home page station card reports (slide 43).
    /// </summary>
    /// <remarks>
    /// One object rather than loose keys because the DFM treats it as one unit: slide 62
    /// says "Link to station card on Home page", so the station ID shown in the
    /// indicator bar, the SKU colour behind it and the Site/Vendor/SW lines on the card
    /// are all the same record read in three places.
    /// </remarks>
    public sealed class StationProfile
    {
        /// <summary>Factory site code, e.g. "ICT KS".</summary>
        public string Site { get; set; }

        /// <summary>Production line, e.g. "Line1".</summary>
        public string Line { get; set; }

        /// <summary>Station identifier, e.g. "FT200". Drawn centred in the indicator bar.</summary>
        public string Station { get; set; }

        /// <summary>
        /// Which machine of that station type this is. Shown after the station name on
        /// the card as "STN01  #1".
        /// </summary>
        public int MachineNumber { get; set; }

        /// <summary>
        /// UI language. Slide 62: "Change language will not require admin access" -
        /// the only field in the block that is not Level 3 gated.
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Product type. Colours the station ID per slide 42; slide 62 requires Level 3
        /// to change it and read-only below that.
        /// </summary>
        public ProductSku ProductType { get; set; }

        /// <summary>
        /// System integrator / vendor, three-letter code. Shown on the card. Fixed by
        /// the software - it is the <c>AAA</c> of <see cref="SoftwareVersion"/> - so the
        /// service fills it in and never stores it.
        /// </summary>
        public string Vendor { get; set; }

        /// <summary>
        /// The SW version string in the DFM's format,
        /// <c>AAA_X.X.X.X_YYMMDD_TYPE</c> (slide 43). Shown in the window title and on
        /// the station card. Slide 43: "SW Version ID is hard coded in SW", so the
        /// service fills it in from the build and never stores it.
        /// </summary>
        public string SoftwareVersion { get; set; }

        /// <summary>
        /// Full path of the main executable. Slide 43: "Main SW path to show the path of
        /// the main SW, click to open the path". The path of the running exe, filled in
        /// by the service and never stored, so it cannot go stale when the software is
        /// installed somewhere else.
        /// </summary>
        public string MainSoftwarePath { get; set; }

        /// <summary>Folder the "Machine Log" button on the station card opens.</summary>
        public string MachineLogPath { get; set; }

        /// <summary>Folder the "HIVE Log" button on the station card opens.</summary>
        public string HiveLogPath { get; set; }

        /// <summary>
        /// Remote qualification mode. Slide 40 gives the station card an orange
        /// background while it is on; slide 63 puts the switch in the hardware dialog.
        /// </summary>
        public bool RemoteQualification { get; set; }

        /// <summary>QPL number shown on the station card.</summary>
        public string QplNumber { get; set; }

        /// <summary>
        /// The hardware type chosen for each subsystem in slide 63's Hardware Setting
        /// dialog, one entry per subsystem in
        /// <see cref="IStationProfileService.HardwareSubsystems"/>.
        /// </summary>
        public List<HardwareSelection> Hardware { get; set; } = new List<HardwareSelection>();

        /// <summary>A copy, so a page can edit a draft without touching the live record.</summary>
        public StationProfile Clone()
        {
            var copy = (StationProfile)MemberwiseClone();
            copy.Hardware = new List<HardwareSelection>();
            if (Hardware != null)
            {
                foreach (HardwareSelection selection in Hardware)
                {
                    if (selection != null)
                        copy.Hardware.Add(new HardwareSelection { Subsystem = selection.Subsystem, Type = selection.Type });
                }
            }

            return copy;
        }

        /// <summary>The chosen type for one subsystem, or null when none is recorded.</summary>
        public string HardwareTypeOf(string subsystem)
        {
            if (Hardware == null)
                return null;

            foreach (HardwareSelection selection in Hardware)
            {
                if (selection != null &&
                    string.Equals(selection.Subsystem, subsystem, StringComparison.OrdinalIgnoreCase))
                {
                    return selection.Type;
                }
            }

            return null;
        }
    }

    /// <summary>One row of the Hardware Setting dialog as saved: a subsystem and its type.</summary>
    public sealed class HardwareSelection
    {
        /// <summary>Subsystem name, e.g. "PLC", "Robot Arm", "CCD".</summary>
        public string Subsystem { get; set; }

        /// <summary>The chosen hardware type, one of that subsystem's options.</summary>
        public string Type { get; set; }
    }

    /// <summary>
    /// One row the Hardware Setting dialog offers (slide 63): a subsystem and the
    /// hardware types it can be. Read from configuration, not compiled in - slide 62:
    /// "Cross QPL/Line/Site software implementation will not allow re-compile software".
    /// </summary>
    public sealed class HardwareSubsystem
    {
        public HardwareSubsystem(string name, IReadOnlyList<string> types)
        {
            Name = name;
            Types = types ?? Array.Empty<string>();
        }

        public string Name { get; }

        /// <summary>The options, in the order the dialog shows them. The first is the default.</summary>
        public IReadOnlyList<string> Types { get; }
    }

    /// <summary>
    /// Reads and writes the station profile, and supplies the option lists the Settings
    /// page's menus are built from.
    /// </summary>
    /// <remarks>
    /// The option lists exist because of slide 62: "Any change should be selected
    /// through the menu bar. Manually key-in is NOT allowed." A free-text box would
    /// violate that, so every one of those fields has to be backed by a closed list.
    /// They are read from configuration, so a new site, line or hardware type needs no
    /// rebuild.
    /// </remarks>
    public interface IStationProfileService
    {
        /// <summary>The live record. Treat as read-only; call <see cref="Save"/> to change it.</summary>
        StationProfile Current { get; }

        /// <summary>Raised after <see cref="Save"/> succeeds, on the caller's thread.</summary>
        event EventHandler ProfileChanged;

        /// <summary>
        /// Persists a profile and republishes <see cref="Current"/>. Returns false when
        /// the write failed, with the reason in <paramref name="error"/> - the Settings
        /// page shows it rather than silently keeping an unsaved edit.
        /// </summary>
        bool Save(StationProfile profile, out string error);

        /// <summary>
        /// SHA-1 of the main executable, as the station card's <c>MS_Hash</c> line.
        /// Slide 43 asks for it to be refreshed on every machine state change, so this
        /// is a method, not a property.
        /// </summary>
        string ComputeMainSoftwareHash();

        IReadOnlyList<string> Sites { get; }
        IReadOnlyList<string> Lines { get; }
        IReadOnlyList<string> Stations { get; }
        IReadOnlyList<string> Languages { get; }
        IReadOnlyList<int> MachineNumbers { get; }

        /// <summary>The rows of slide 63's Hardware Setting dialog.</summary>
        IReadOnlyList<HardwareSubsystem> HardwareSubsystems { get; }
    }
}
