using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="IAccessControlAdmin"/> over two files: the local access list
    /// (<c>Data\AccessDatabase.json</c>, the file <see cref="JsonAccessControlService"/>
    /// checks badges against) and the settings (<c>Data\Settings\AccessControl.json</c>).
    /// </summary>
    public sealed class AccessControlAdmin : IAccessControlAdmin
    {
        private const string SettingsFileName = "AccessControl.json";

        /// <summary>The CSV columns, in Access Control V5.1's order and spelling.</summary>
        private static readonly string[] CsvColumns =
        {
            "Site", "Project", "Station_ID", "terminalName", "Name", "Access_Level", "SW_Type", "Card_UID", "Function", "Organization",
        };

        private readonly string _listPath;
        private readonly string _settingsPath;
        private readonly int _badgeLength;
        private readonly object _gate = new object();

        private AccessControlSettings _settings;

        /// <param name="badgeLength">
        /// The fixed badge UID length (App.config BadgeUidLength), or 0 when it varies. A
        /// numeric UID imported shorter than this gets back the leading zeros Excel drops.
        /// </param>
        public AccessControlAdmin(string dataDirectory, bool mesAvailable, int badgeLength)
        {
            _listPath = Path.Combine(dataDirectory ?? string.Empty, JsonAccessControlService.FileName);
            _settingsPath = Path.Combine(dataDirectory ?? string.Empty, "Settings", SettingsFileName);
            _badgeLength = badgeLength;
            IsMesAvailable = mesAvailable;
            _settings = ReadSettings();
        }

        public event EventHandler SettingsChanged;

        public AccessControlSettings Settings
        {
            get
            {
                lock (_gate)
                {
                    return _settings.Clone();
                }
            }
        }

        public bool IsMesAvailable { get; }

        public bool SaveSettings(AccessControlSettings settings, out string error)
        {
            error = null;
            if (settings == null)
            {
                error = "No settings to save.";
                return false;
            }

            AccessControlSettings copy = settings.Clone();
            copy.AutomaticSignOutMinutes = ClampMinutes(copy.AutomaticSignOutMinutes);
            if (copy.AccessControlWithMes && !IsMesAvailable)
            {
                error = "MES is not configured on this machine (MesUrl in App.config), so badges cannot be checked with MES.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(copy, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            lock (_gate)
            {
                _settings = copy;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public IReadOnlyList<AccessRecord> LoadRecords(out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(_listPath))
                    return new List<AccessRecord>();

                List<AccessRecord> records = JsonConvert.DeserializeObject<List<AccessRecord>>(File.ReadAllText(_listPath));
                return (records ?? new List<AccessRecord>()).Where(r => r != null).ToList();
            }
            catch (Exception ex)
            {
                error = "The access list could not be read: " + ex.Message;
                return new List<AccessRecord>();
            }
        }

        public bool SaveRecords(IReadOnlyList<AccessRecord> records, out string error)
        {
            error = Validate(records);
            if (error != null)
                return false;

            try
            {
                // Written to a temporary file first, so a failed write cannot leave the
                // list half written - it is what every badge check reads.
                string temporary = _listPath + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(records, Formatting.Indented), Encoding.UTF8);
                if (File.Exists(_listPath))
                    File.Replace(temporary, _listPath, null);
                else
                    File.Move(temporary, _listPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "The access list could not be saved: " + ex.Message;
                return false;
            }
        }

        public IReadOnlyList<AccessRecord> ImportCsv(string path, out string error, out string notes)
        {
            error = null;
            notes = null;

            List<string[]> lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(SplitCsvLine)
                    .ToList();
            }
            catch (Exception ex)
            {
                error = "The file could not be read: " + ex.Message;
                return null;
            }

            if (lines.Count < 2)
            {
                error = "The file has no records under its header line.";
                return null;
            }

            Dictionary<string, int> columns = MapColumns(lines[0]);
            string[] required = { "cardUid", "terminalName", "accessLevel", "softwareType" };
            string[] missing = required.Where(c => !columns.ContainsKey(c)).ToArray();
            if (missing.Length > 0)
            {
                error = "The header line is missing " + string.Join(", ", missing.Select(ColumnTitle))
                    + ". Expected: " + string.Join(",", CsvColumns) + ".";
                return null;
            }

            var records = new List<AccessRecord>();
            int padded = 0;
            for (int i = 1; i < lines.Count; i++)
            {
                string[] cells = lines[i];
                string Cell(string key) => columns.TryGetValue(key, out int index) && index < cells.Length ? cells[index].Trim() : string.Empty;

                int level;
                if (!int.TryParse(Cell("accessLevel"), out level) || level < 1 || level > 3)
                {
                    error = "Line " + (i + 1) + ": Access_Level must be 1, 2 or 3, not \"" + Cell("accessLevel") + "\".";
                    return null;
                }

                string uid = Cell("cardUid");
                if (_badgeLength > 0 && uid.Length > 0 && uid.Length < _badgeLength && uid.All(char.IsDigit))
                {
                    uid = uid.PadLeft(_badgeLength, '0');
                    padded++;
                }

                AccessOrganization organization;
                Enum.TryParse(Cell("organization"), true, out organization);

                records.Add(new AccessRecord
                {
                    Site = Cell("site"),
                    Project = Cell("project"),
                    StationId = Cell("stationId"),
                    TerminalName = Cell("terminalName").ToUpperInvariant(),
                    Name = Cell("name"),
                    AccessLevel = level,
                    SoftwareType = Cell("softwareType"),
                    CardUid = uid,
                    Function = Cell("function"),
                    Organization = organization,
                });
            }

            error = Validate(records);
            if (error != null)
                return null;

            if (padded > 0)
            {
                notes = padded + " badge UID" + (padded == 1 ? " was" : "s were") + " shorter than " + _badgeLength
                    + " digits and had the leading zeros Excel drops put back.";
            }

            return records;
        }

        public bool ExportCsv(string path, IReadOnlyList<AccessRecord> records, out string error)
        {
            error = null;
            try
            {
                var text = new StringBuilder();
                text.AppendLine(string.Join(",", CsvColumns));
                foreach (AccessRecord r in records ?? new List<AccessRecord>())
                {
                    text.AppendLine(string.Join(",", new[]
                    {
                        r.Site, r.Project, r.StationId, r.TerminalName, r.Name, r.AccessLevel.ToString(),
                        r.SoftwareType, r.CardUid, r.Function, r.Organization.ToString(),
                    }.Select(Quote)));
                }

                // With a byte order mark, so Excel opens Chinese names correctly.
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Null when every record can be saved; otherwise what is wrong with the first bad one.</summary>
        private static string Validate(IReadOnlyList<AccessRecord> records)
        {
            if (records == null)
                return "No records to save.";

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records.Count; i++)
            {
                AccessRecord r = records[i];
                string where = "Record " + (i + 1) + (string.IsNullOrWhiteSpace(r.Name) ? "" : " (" + r.Name + ")");

                if (string.IsNullOrWhiteSpace(r.CardUid))
                    return where + " has no Card_UID.";
                if (string.IsNullOrWhiteSpace(r.TerminalName))
                    return where + " has no terminalName.";
                if (string.IsNullOrWhiteSpace(r.SoftwareType))
                    return where + " has no SW_Type.";
                if (JsonAccessControlService.ToLevel(r.AccessLevel) == AccessLevel.None)
                    return where + " has Access_Level " + r.AccessLevel + "; it must be 1, 2 or 3.";

                string key = r.CardUid.Trim() + "|" + r.TerminalName.Trim() + "|" + r.SoftwareType.Trim();
                if (!seen.Add(key))
                    return where + ": the same card is listed twice for " + r.TerminalName + " (" + r.SoftwareType + ").";
            }

            return null;
        }

        private AccessControlSettings ReadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    // Replace, not append to, the lists the settings class starts with.
                    AccessControlSettings read = JsonConvert.DeserializeObject<AccessControlSettings>(
                        File.ReadAllText(_settingsPath),
                        new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                    if (read != null)
                    {
                        read.AutomaticSignOutMinutes = ClampMinutes(read.AutomaticSignOutMinutes);
                        if (read.AccessControlWithMes && !IsMesAvailable)
                            read.AccessControlWithMes = false;
                        return read;
                    }
                }
            }
            catch (Exception)
            {
                // An unreadable settings file falls back to the defaults: local checking, five minutes.
            }

            return new AccessControlSettings();
        }

        /// <summary>Slide 39's five minutes is the longest allowed.</summary>
        private static int ClampMinutes(int minutes)
        {
            return minutes < 1 || minutes > 5 ? 5 : minutes;
        }

        private static Dictionary<string, int> MapColumns(string[] header)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                string key = Normalise(header[i]);
                if (key != null && !map.ContainsKey(key))
                    map[key] = i;
            }

            return map;
        }

        /// <summary>Accepts the V5.1 spellings and the JSON property names alike.</summary>
        private static string Normalise(string title)
        {
            string t = (title ?? string.Empty).Trim().Trim('﻿').Replace("_", "").Replace(" ", "").ToLowerInvariant();
            switch (t)
            {
                case "site": return "site";
                case "project": return "project";
                case "stationid": case "processstationid": return "stationId";
                case "terminalname": return "terminalName";
                case "name": case "neme": return "name";
                case "accesslevel": case "level": return "accessLevel";
                case "swtype": case "softwaretype": return "softwareType";
                case "carduid": case "uid": return "cardUid";
                case "function": return "function";
                case "organization": case "organisation": case "org": return "organization";
                default: return null;
            }
        }

        private static string ColumnTitle(string key)
        {
            switch (key)
            {
                case "cardUid": return "Card_UID";
                case "terminalName": return "terminalName";
                case "accessLevel": return "Access_Level";
                case "softwareType": return "SW_Type";
                default: return key;
            }
        }

        private static string[] SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        cell.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else
                {
                    cell.Append(c);
                }
            }

            cells.Add(cell.ToString());
            return cells.ToArray();
        }

        private static string Quote(string value)
        {
            string cell = value ?? string.Empty;
            return cell.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
        }
    }
}
