using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The access-control settings of Access Control V5.1 section 3.3.1 (the Setting
    /// file): where badges are checked, and when an idle operator is signed out.
    /// </summary>
    public sealed class AccessControlSettings
    {
        /// <summary>
        /// "Access Control with MES": true checks badges with MES
        /// (G_MACHINE_ACCESS_BY_USERNO), false with the local access list.
        /// </summary>
        public bool AccessControlWithMes { get; set; }

        /// <summary>"Project Code", e.g. H38 - sent to MES with every badge check.</summary>
        public string ProjectCode { get; set; } = string.Empty;

        /// <summary>The project codes the Setting page offers.</summary>
        public List<string> ProjectCodes { get; set; } = new List<string> { "H36", "H37", "H38", "H39", "H58", "H59" };

        /// <summary>
        /// "Automatically Sign out", in minutes, for Level 2 and 3 (DFM slide 39 sets five
        /// as the longest).
        /// </summary>
        public int AutomaticSignOutMinutes { get; set; } = 5;

        /// <summary>
        /// Function names beginning with one of these belong to the CM, e.g. "ICT_DFX". MES
        /// returns a function but not an organisation, and the two-step rule of slide 45
        /// depends on it.
        /// </summary>
        public List<string> CmFunctionPrefixes { get; set; } = new List<string> { "ICT", "CM" };

        /// <summary>Function names beginning with one of these are Apple's, e.g. "Apple_AME".</summary>
        public List<string> AppleFunctionPrefixes { get; set; } = new List<string> { "Apple" };

        public AccessControlSettings Clone()
        {
            return new AccessControlSettings
            {
                AccessControlWithMes = AccessControlWithMes,
                ProjectCode = ProjectCode,
                ProjectCodes = new List<string>(ProjectCodes ?? new List<string>()),
                AutomaticSignOutMinutes = AutomaticSignOutMinutes,
                CmFunctionPrefixes = new List<string>(CmFunctionPrefixes ?? new List<string>()),
                AppleFunctionPrefixes = new List<string>(AppleFunctionPrefixes ?? new List<string>()),
            };
        }
    }

    /// <summary>
    /// Maintains the local access list and the access-control settings - the Access
    /// Control tab of the Setting page (Access Control V5.1, S1.2.2 and S1.2.3).
    /// </summary>
    public interface IAccessControlAdmin
    {
        AccessControlSettings Settings { get; }

        /// <summary>True when an MES address is configured, so MES checking can be switched on.</summary>
        bool IsMesAvailable { get; }

        /// <summary>Raised after the settings were saved.</summary>
        event EventHandler SettingsChanged;

        bool SaveSettings(AccessControlSettings settings, out string error);

        /// <summary>Every record of the local access list; empty with the reason when it cannot be read.</summary>
        IReadOnlyList<AccessRecord> LoadRecords(out string error);

        bool SaveRecords(IReadOnlyList<AccessRecord> records, out string error);

        /// <summary>Reads records from a CSV with the V5.1 column names; null with the reason when it cannot.</summary>
        /// <param name="notes">What was adjusted on the way in, e.g. badge UIDs padded with the zeros Excel drops.</param>
        IReadOnlyList<AccessRecord> ImportCsv(string path, out string error, out string notes);

        bool ExportCsv(string path, IReadOnlyList<AccessRecord> records, out string error);
    }
}
