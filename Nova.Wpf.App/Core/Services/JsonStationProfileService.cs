using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Station profile stored as <c>Data\Settings\StationProfile.json</c>, alongside the
    /// recipe and machine-settings JSON the other screens already write.
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
    /// falls back to <see cref="BuildDefault"/> and the reason goes to Debug.
    /// </para>
    /// </remarks>
    public sealed class JsonStationProfileService : IStationProfileService
    {
        private const string SettingsFolder = "Settings";
        private const string ProfileFile = "StationProfile.json";

        private readonly string _dataDirectory;
        private readonly object _gate = new object();

        private StationProfile _current;

        public event EventHandler ProfileChanged;

        public StationProfile Current
        {
            get { lock (_gate) { return _current; } }
        }

        // Closed option lists, because slide 62 forbids typing these in. Kept here
        // rather than in the view model so a site can extend them without a rebuild of
        // the presentation layer, and so the Settings page cannot invent a value that
        // is not offered.
        public IReadOnlyList<string> Sites { get; }
        public IReadOnlyList<string> Lines { get; }
        public IReadOnlyList<string> Stations { get; }
        public IReadOnlyList<string> Languages { get; }

        public JsonStationProfileService(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("Data directory cannot be empty.", nameof(dataDirectory));

            _dataDirectory = dataDirectory;
            _current = Load();

            Sites = new[] { "ICT KS", "ICT SZ", "PEGA KS", "LUXSHARE" };
            Lines = new[] { "Line1", "Line2", "Line3", "Line4" };
            Stations = new[] { "FT200", "FT300", "FT400", "BE010", "BE020", "CP0100" };
            Languages = new[] { "English", "简体中文" };
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
                string path = ProfilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                string json = JsonConvert.SerializeObject(profile, Formatting.Indented);

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
            string path = Current != null ? Current.MainSoftwarePath : null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                path = MainExecutablePath();

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

            return BuildDefault();
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
            if (string.IsNullOrWhiteSpace(profile.Vendor)) profile.Vendor = fallback.Vendor;
            if (string.IsNullOrWhiteSpace(profile.SoftwareVersion)) profile.SoftwareVersion = fallback.SoftwareVersion;
            if (string.IsNullOrWhiteSpace(profile.MainSoftwarePath)) profile.MainSoftwarePath = fallback.MainSoftwarePath;
            if (string.IsNullOrWhiteSpace(profile.MachineLogPath)) profile.MachineLogPath = fallback.MachineLogPath;
            if (string.IsNullOrWhiteSpace(profile.HiveLogPath)) profile.HiveLogPath = fallback.HiveLogPath;
            if (string.IsNullOrWhiteSpace(profile.QplNumber)) profile.QplNumber = fallback.QplNumber;
            if (profile.MachineNumber <= 0) profile.MachineNumber = fallback.MachineNumber;

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
                Vendor = "TET",

                // Placeholder in the DFM's own format so the field is self-documenting
                // until a build pipeline stamps the real one in. Suffixed VAL, not POR:
                // labelling an un-stamped build "production of record" is the one wrong
                // default here.
                SoftwareVersion = "TET_1.0.0.0_" + DateTime.Now.ToString("yyMMdd") + "_VAL",

                MainSoftwarePath = MainExecutablePath(),
                MachineLogPath = @"C:\TEAL_Log_Data",
                HiveLogPath = @"C:\TEAL_Log_Data\HIVE",
                RemoteQualification = false,
                QplNumber = "#1"
            };
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
    }
}
