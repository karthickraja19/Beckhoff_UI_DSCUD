using System;
using System.IO;
using System.Xml.Linq;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Reads the machine identity from Data\config.txt (an XML document despite the
    /// extension) — the same file the WinForms app's UIHelper.LoadMachineDetails() read.
    ///
    /// Elements are looked up BY NAME, not by child index. V3 indexed into
    /// DocumentElement.ChildNodes, and indices 9 and 10 — which it assigned to "program"
    /// and "version" — actually land on CCDTriggerRequired and CCDResultRequired, so the
    /// version it carried was the string "true". Reading by name also survives anyone
    /// adding or reordering an element in the profile.
    ///
    /// Read once at construction: the profile does not change while the app runs, and the
    /// title bar should not be doing file IO on every binding refresh.
    /// </summary>
    public class ConfigFileMachineIdentityService : IMachineIdentityService
    {
        private const string Unknown = "Unknown";

        public string MachineName { get; private set; }
        public string SoftwareVersion { get; private set; }
        public string MachineVersion { get; private set; }

        public string DisplayVersion
        {
            get
            {
                // "Version 1 (V57)" when both are known; drop the parenthetical when the
                // machine variant is missing rather than printing an empty bracket.
                if (string.IsNullOrWhiteSpace(MachineVersion) || MachineVersion == Unknown)
                    return "Version " + SoftwareVersion;

                return "Version " + SoftwareVersion + " (" + MachineVersion + ")";
            }
        }

        public ConfigFileMachineIdentityService()
        {
            MachineName = Unknown;
            SoftwareVersion = Unknown;
            MachineVersion = Unknown;

            // A missing or malformed profile must never stop the app starting — the
            // machine still runs, the title bar just cannot name it.
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "config.txt");
                if (!File.Exists(path))
                    return;

                var root = XDocument.Load(path).Root;
                if (root == null)
                    return;

                MachineName = Read(root, "MachineName", Unknown);
                SoftwareVersion = Read(root, "SWVersion", Unknown);
                MachineVersion = Read(root, "MCVersion", Unknown);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Machine identity load failed: " + ex.Message);
            }
        }

        private static string Read(XElement root, string name, string fallback)
        {
            var element = root.Element(name);
            if (element == null)
                return fallback;

            string value = element.Value;
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
