using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="IHashKeyService"/>: the file behind each hash key is kept in
    /// <c>Data\Settings\HashKeys.json</c>, and each key's SHA-1 is read live.
    /// </summary>
    /// <remarks>
    /// A file is only hashed again when its time or size changed, so refreshing the keys
    /// on every state change (slide 43) does not re-read a large executable each time.
    /// </remarks>
    public sealed class HashKeyService : IHashKeyService
    {
        private const string FileName = "HashKeys.json";

        /// <summary>SAVE_AUDIO_INFO: MES stores this station's hash keys (S3.6).</summary>
        public const string SaveCommand = "SAVE_AUDIO_INFO";

        private readonly string _pathsFile;
        private readonly IMesClient _mes;
        private readonly Func<string> _site;
        private readonly Func<string> _project;
        private readonly object _gate = new object();
        private readonly Dictionary<string, CachedHash> _cache = new Dictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, string> _paths;
        private IReadOnlyList<HashKeyReading> _latest = new List<HashKeyReading>();

        /// <param name="defaults">Files set when there is no paths file yet, e.g. MS_SHA1 = this executable.</param>
        public HashKeyService(string dataDirectory, IMesClient mes, Func<string> site, Func<string> project, IDictionary<string, string> defaults)
        {
            _pathsFile = Path.Combine(dataDirectory ?? string.Empty, "Settings", FileName);
            _mes = mes;
            _site = site;
            _project = project;
            Definitions = BuildDefinitions();
            _paths = ReadPaths(defaults);
        }

        public event EventHandler PathsChanged;

        public IReadOnlyList<HashKeyDefinition> Definitions { get; }

        public IReadOnlyDictionary<string, string> Paths
        {
            get
            {
                lock (_gate)
                {
                    return new Dictionary<string, string>(_paths, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public IReadOnlyList<HashKeyReading> Latest
        {
            get
            {
                lock (_gate)
                {
                    return _latest;
                }
            }
        }

        public bool SavePaths(IDictionary<string, string> paths, out string error)
        {
            error = null;
            var clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (HashKeyDefinition definition in Definitions)
            {
                string path;
                if (paths != null && paths.TryGetValue(definition.Key, out path) && !string.IsNullOrWhiteSpace(path))
                    clean[definition.Key] = path.Trim();
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_pathsFile));
                File.WriteAllText(_pathsFile, JsonConvert.SerializeObject(clean, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = "The file paths could not be saved: " + ex.Message;
                return false;
            }

            lock (_gate)
            {
                _paths = clean;
            }

            PathsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public IReadOnlyList<HashKeyReading> ReadAll()
        {
            Dictionary<string, string> paths;
            lock (_gate)
            {
                paths = new Dictionary<string, string>(_paths, StringComparer.OrdinalIgnoreCase);
            }

            var readings = new List<HashKeyReading>();
            foreach (HashKeyDefinition definition in Definitions)
            {
                string path;
                if (!paths.TryGetValue(definition.Key, out path) || string.IsNullOrWhiteSpace(path))
                    continue;

                readings.Add(Read(definition.Key, path));
            }

            lock (_gate)
            {
                _latest = readings;
            }

            return readings;
        }

        public bool ExportCsv(string path, out string error)
        {
            error = null;
            try
            {
                var byKey = ReadAll().ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
                var text = new StringBuilder();
                text.AppendLine("Key,Controls,FilePath,Value,Error");
                foreach (HashKeyDefinition definition in Definitions)
                {
                    HashKeyReading reading;
                    byKey.TryGetValue(definition.Key, out reading);
                    text.AppendLine(string.Join(",", new[]
                    {
                        definition.Key,
                        definition.Controls,
                        reading != null ? reading.FilePath : string.Empty,
                        reading != null ? reading.Value : string.Empty,
                        reading != null ? reading.Error : string.Empty,
                    }.Select(Quote)));
                }

                File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public Task<MesResult> UploadToMesAsync(string approverId)
        {
            List<HashKeyReading> keys = ReadAll().Where(r => r.Value != null).ToList();
            if (keys.Count == 0)
                return Task.FromResult(MesResult.NotReached("No hash key could be read, so there is nothing to upload."));

            // S3.6: results is key=NAME$value=HASH, comma separated with no comma after the last.
            string results = string.Join(",", keys.Select(k => "key=" + k.Key + "$value=" + k.Value));
            string testData = "site=" + Safe(_site) + "$project=" + Safe(_project) + "$approver_id=" + (approverId ?? string.Empty);

            // serial_Number may not be empty; S3.6 says to send the text "SN".
            return _mes.GetCmdResultAsync(SaveCommand, "SN", testData, results);
        }

        private HashKeyReading Read(string key, string path)
        {
            var reading = new HashKeyReading { Key = key, FilePath = path };

            try
            {
                string folder = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    reading.Error = "The folder does not exist on this IPC: " + folder;
                    return reading;
                }

                if (!File.Exists(path))
                {
                    reading.Error = "The file was not found: " + path;
                    return reading;
                }

                var info = new FileInfo(path);
                string stamp = info.LastWriteTimeUtc.Ticks + "|" + info.Length;

                lock (_gate)
                {
                    CachedHash cached;
                    if (_cache.TryGetValue(path, out cached) && cached.Stamp == stamp)
                    {
                        reading.Value = cached.Value;
                        return reading;
                    }
                }

                string value;
                using (var sha = SHA1.Create())
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] bytes = sha.ComputeHash(stream);
                    var builder = new StringBuilder(bytes.Length * 2);
                    foreach (byte b in bytes)
                        builder.Append(b.ToString("X2"));
                    value = builder.ToString();
                }

                // Slide 91: a hash key that is not 40 hexadecimal characters is an error.
                if (value.Length != 40)
                {
                    reading.Error = "The hash key is not 40 hexadecimal characters.";
                    return reading;
                }

                lock (_gate)
                {
                    _cache[path] = new CachedHash { Stamp = stamp, Value = value };
                }

                reading.Value = value;
            }
            catch (Exception ex)
            {
                reading.Error = "The file could not be read: " + ex.Message;
            }

            return reading;
        }

        private Dictionary<string, string> ReadPaths(IDictionary<string, string> defaults)
        {
            try
            {
                if (File.Exists(_pathsFile))
                {
                    var read = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_pathsFile));
                    if (read != null)
                        return new Dictionary<string, string>(read, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                // An unreadable file falls back to the defaults, which the PD-15 page then shows.
            }

            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (defaults != null)
            {
                foreach (var pair in defaults)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Value))
                        paths[pair.Key] = pair.Value;
                }
            }

            return paths;
        }

        /// <summary>The keys of slide 12, in its order.</summary>
        private static IReadOnlyList<HashKeyDefinition> BuildDefinitions()
        {
            var keys = new List<HashKeyDefinition>
            {
                new HashKeyDefinition("MS_SHA1", "Main SW"),
                new HashKeyDefinition("VS_SHA1", "Vision SW"),
                new HashKeyDefinition("LS_SHA1", "Laser SW"),
                new HashKeyDefinition("RS_SHA1", "Robot SW"),
                new HashKeyDefinition("IS_SHA1", "IDE"),
                new HashKeyDefinition("PLC_SHA1", "PLC Program"),
                new HashKeyDefinition("CD_SHA1", "Main SW Access Control"),
                new HashKeyDefinition("CD2_SHA1", "Vision SW Access Control"),
            };

            for (int i = 3; i <= 30; i++)
                keys.Add(new HashKeyDefinition("CD" + i + "_SHA1", "Glue Path Files"));
            for (int i = 31; i <= 40; i++)
                keys.Add(new HashKeyDefinition("CD" + i + "_SHA1", "Robot/Gantry Speeds/Acceleration"));
            for (int i = 1; i <= 20; i++)
                keys.Add(new HashKeyDefinition("VL" + i + "_SHA1", "Vision Positioning/Recheck Template"));
            for (int i = 1; i <= 20; i++)
                keys.Add(new HashKeyDefinition("VR" + i + "_SHA1", "Vision/AOI Recheck Template"));

            return keys;
        }

        private static string Safe(Func<string> value)
        {
            try
            {
                return value != null ? value() ?? string.Empty : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string Quote(string value)
        {
            string cell = value ?? string.Empty;
            return cell.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
        }

        private sealed class CachedHash
        {
            public string Stamp;
            public string Value;
        }
    }
}
