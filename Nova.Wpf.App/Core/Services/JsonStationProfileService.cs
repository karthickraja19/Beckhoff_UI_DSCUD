using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.Core.Config;

namespace Nova.Core.Services
{
    /// <summary>
    /// Station profile stored as <c>Data\Settings\StationProfile.json</c>, alongside the
    /// recipe and machine-settings JSON the other screens already write. The Settings
    /// page's option lists come from <c>Data\Settings\StationOptions.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately separate from <see cref="IMachineIdentityService"/>, which reads the
    /// fixed installation facts out of <c>Data\config.txt</c> once at startup and never
    /// writes. This record is operator-editable from the Settings page, so it needs its
    /// own writable file rather than a round-trip through the XML profile the WinForms
    /// app also reads.
    /// </para>
    /// <para>
    /// A missing, empty or malformed file is not an error: the machine still has to
    /// start, and a blank indicator bar tells the operator nothing. Every load failure
    /// falls back to <see cref="BuildDefault"/> and the reason goes to Debug. The same
    /// holds for the options file: any list it leaves out keeps its built-in default.
    /// </para>
    /// <para>
    /// Vendor, SW version and main SW path are never stored. Slide 43 hard codes the
    /// version in the software, the vendor is that version's first field, and the path
    /// is wherever the exe is running from - a stored copy of any of them could only
    /// ever disagree with the build. They are filled in on load and stripped on save.
    /// </para>
    /// </remarks>
    public sealed class JsonStationProfileService : IStationProfileService
    {
        private const string SettingsFolder = "Settings";
        private const string ProfileFile = "StationProfile.json";
        private const string OptionsFile = "StationOptions.json";

        // Filled in from the build on every load, so never written; see the remarks.
        private static readonly string[] FixedFields = { "Vendor", "SoftwareVersion", "MainSoftwarePath" };

        private readonly string _dataDirectory;
        private readonly object _gate = new object();

        private StationProfile _current;

        public event EventHandler ProfileChanged;

        public StationProfile Current
        {
            get { lock (_gate) { return _current; } }
        }

        // Closed option lists, because slide 62 forbids typing these in. Read from
        // StationOptions.json so a site can extend them without a rebuild (slide 62:
        // "Cross QPL/Line/Site software implementation will not allow re-compile
        // software"), and so the Settings page cannot invent a value that is not offered.
        public IReadOnlyList<string> Sites { get; private set; }
        public IReadOnlyList<string> Lines { get; private set; }
        public IReadOnlyList<string> Stations { get; private set; }
        public IReadOnlyList<string> Languages { get; private set; }
        public IReadOnlyList<int> MachineNumbers { get; private set; }
        public IReadOnlyList<HardwareSubsystem> HardwareSubsystems { get; private set; }

        public JsonStationProfileService(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("Data directory cannot be empty.", nameof(dataDirectory));

            _dataDirectory = dataDirectory;

            // Options first: loading the profile fills in a default hardware type for
            // any subsystem the stored file does not mention.
            LoadOptions();
            _current = Load();
        }

        public bool Save(StationProfile profile, out string error)
        {
            error = null;

            if (profile == null)
            {
                error = "No profile to save.";
                return false;
            }

            try
            {
                ApplyFixedFields(profile);

                string path = ProfilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                string json = Serialize(profile);

                // Written through a temp file and then moved into place: an operator
                // power-cycling the panel PC mid-save must not be able to leave a
                // half-written profile that fails to parse on the next start.
                string temp = path + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(path))
                    File.Delete(path);

                File.Move(temp, path);

                lock (_gate)
                {
                    _current = profile;
                }

                var handler = ProfileChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                System.Diagnostics.Debug.WriteLine("Station profile save failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// SHA-1 of the running executable, formatted as the lower-case hex string the
        /// DFM's station card shows.
        /// </summary>
        /// <remarks>
        /// SHA-1 because the DFM names the keys <c>MS_SHA1</c>, <c>VS_SHA1</c> and so on
        /// (slide 12) - it is a change-detection fingerprint reported to HIVE, not a
        /// security primitive, and the algorithm is fixed by the spec. Hashing a
        /// multi-megabyte exe on every machine state change would be wasteful, so the
        /// result is cached against the file's last-write time and size and only
        /// recomputed when either moves.
        /// </remarks>
        public string ComputeMainSoftwareHash()
        {
            string path = MainExecutablePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "unavailable";

            try
            {
                var info = new FileInfo(path);
                string stamp = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;

                lock (_gate)
                {
                    if (_hashStamp == stamp && _hash != null)
                        return _hash;
                }

                string computed;
                using (var sha = SHA1.Create())
                using (var stream = File.OpenRead(path))
                {
                    byte[] bytes = sha.ComputeHash(stream);
                    var builder = new StringBuilder(bytes.Length * 2);
                    foreach (byte b in bytes)
                        builder.Append(b.ToString("x2"));
                    computed = builder.ToString();
                }

                lock (_gate)
                {
                    _hashStamp = stamp;
                    _hash = computed;
                }

                return computed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Main SW hash failed: " + ex.Message);
                return "unavailable";
            }
        }

        private string _hash;
        private string _hashStamp;

        private string ProfilePath()
        {
            return Path.Combine(_dataDirectory, SettingsFolder, ProfileFile);
        }

        private StationProfile Load()
        {
            try
            {
                string path = ProfilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonConvert.DeserializeObject<StationProfile>(json);
                        if (loaded != null)
                            return Normalise(loaded);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Station profile load failed: " + ex.Message);
            }

            return Normalise(BuildDefault());
        }

        /// <summary>
        /// Fills in anything the stored file left blank, so a profile written by an
        /// older build (or hand-edited) still produces a renderable indicator bar and
        /// station card.
        /// </summary>
        private StationProfile Normalise(StationProfile profile)
        {
            StationProfile fallback = BuildDefault();

            if (string.IsNullOrWhiteSpace(profile.Site)) profile.Site = fallback.Site;
            if (string.IsNullOrWhiteSpace(profile.Line)) profile.Line = fallback.Line;
            if (string.IsNullOrWhiteSpace(profile.Station)) profile.Station = fallback.Station;
            if (string.IsNullOrWhiteSpace(profile.Language)) profile.Language = fallback.Language;
            if (string.IsNullOrWhiteSpace(profile.MachineLogPath)) profile.MachineLogPath = fallback.MachineLogPath;
            if (string.IsNullOrWhiteSpace(profile.HiveLogPath)) profile.HiveLogPath = fallback.HiveLogPath;
            if (string.IsNullOrWhiteSpace(profile.QplNumber)) profile.QplNumber = fallback.QplNumber;
            if (profile.MachineNumber <= 0) profile.MachineNumber = fallback.MachineNumber;

            // A subsystem with no recorded type takes its first option, which is the
            // default the options file lists first.
            if (profile.Hardware == null)
                profile.Hardware = new List<HardwareSelection>();
            profile.Hardware.RemoveAll(h => h == null ||
                                            string.IsNullOrWhiteSpace(h.Subsystem) ||
                                            string.IsNullOrWhiteSpace(h.Type));
            foreach (HardwareSubsystem subsystem in HardwareSubsystems)
            {
                if (profile.HardwareTypeOf(subsystem.Name) == null && subsystem.Types.Count > 0)
                    profile.Hardware.Add(new HardwareSelection { Subsystem = subsystem.Name, Type = subsystem.Types[0] });
            }

            ApplyFixedFields(profile);
            return profile;
        }

        private static StationProfile BuildDefault()
        {
            return new StationProfile
            {
                Site = "ICT KS",
                Line = "Line1",
                Station = "FT200",
                MachineNumber = 1,
                Language = "English",
                ProductType = ProductSku.Common,
                MachineLogPath = @"C:\TEAL_Log_Data",
                HiveLogPath = @"C:\TEAL_Log_Data\HIVE",
                RemoteQualification = false,
                QplNumber = "#1"
            };
        }

        /// <summary>The three fields the build owns; see the class remarks.</summary>
        private static void ApplyFixedFields(StationProfile profile)
        {
            profile.Vendor = SoftwareRelease.Vendor;
            profile.SoftwareVersion = SoftwareRelease.Version;
            profile.MainSoftwarePath = MainExecutablePath();
        }

        /// <summary>
        /// Indented JSON, product type by name rather than number so the file can be
        /// read and edited by hand, and without the fields the build owns.
        /// </summary>
        private static string Serialize(StationProfile profile)
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new StringEnumConverter());

            JObject json = JObject.FromObject(profile, JsonSerializer.Create(settings));
            foreach (string field in FixedFields)
                json.Remove(field);

            return json.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Reads the option lists. A list the file leaves out, or leaves empty, keeps
        /// its built-in default, so a partial or missing file still gives a usable page.
        /// </summary>
        private void LoadOptions()
        {
            StationOptionsFile file = null;
            try
            {
                string path = Path.Combine(_dataDirectory, SettingsFolder, OptionsFile);
                if (File.Exists(path))
                    file = JsonConvert.DeserializeObject<StationOptionsFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Station options load failed: " + ex.Message);
            }

            if (file == null)
                file = new StationOptionsFile();

            Sites = OrDefault(file.Sites, "ICT KS", "ICT SZ", "PEGA KS", "LUXSHARE");
            Lines = OrDefault(file.Lines, "Line1", "Line2", "Line3", "Line4");
            Stations = OrDefault(file.Stations, "FT200", "FT300", "FT400", "BE010", "BE020", "CP0100");
            Languages = OrDefault(file.Languages, "English", "简体中文");

            List<int> numbers = (file.MachineNumbers ?? new List<int>()).Where(n => n > 0).Distinct().ToList();
            MachineNumbers = numbers.Count > 0 ? numbers : Enumerable.Range(1, 8).ToList();

            // No built-in hardware rows: which subsystems a machine has, and what each
            // can be, is per machine, and inventing rows would put choices on screen
            // that change nothing.
            var subsystems = new List<HardwareSubsystem>();
            foreach (HardwareOptionsEntry entry in file.Hardware ?? new List<HardwareOptionsEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Subsystem))
                    continue;

                List<string> types = Clean(entry.Types);
                if (types.Count > 0)
                    subsystems.Add(new HardwareSubsystem(entry.Subsystem.Trim(), types));
            }
            HardwareSubsystems = subsystems;
        }

        private static IReadOnlyList<string> OrDefault(List<string> values, params string[] fallback)
        {
            List<string> cleaned = Clean(values);
            return cleaned.Count > 0 ? cleaned : fallback.ToList();
        }

        private static List<string> Clean(List<string> values)
        {
            return (values ?? new List<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string MainExecutablePath()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly();
                if (assembly != null && !string.IsNullOrWhiteSpace(assembly.Location))
                    return assembly.Location;

                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Nova.Wpf.App.exe");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Shape of StationOptions.json.</summary>
        internal sealed class StationOptionsFile
        {
            public List<string> Sites { get; set; }
            public List<string> Lines { get; set; }
            public List<string> Stations { get; set; }
            public List<string> Languages { get; set; }
            public List<int> MachineNumbers { get; set; }
            public List<HardwareOptionsEntry> Hardware { get; set; }
        }

        /// <summary>One Hardware Setting row in StationOptions.json.</summary>
        internal sealed class HardwareOptionsEntry
        {
            public string Subsystem { get; set; }
            public List<string> Types { get; set; }
        }
    }
}
