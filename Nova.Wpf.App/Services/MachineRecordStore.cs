using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// The machine's event records that are not in TEAL_Master - glue weighings and measured
    /// coordinates - as one CSV file per kind and day: <c>{folder}\{kind}\{kind}_yyyyMMdd.csv</c>.
    /// The PLC keeps only the last event; the HMI keeps the history the Data page shows.
    /// </summary>
    /// <remarks>
    /// Plain files, so they are readable without the HMI and survive a database outage.
    /// Appends are serialised per store. Values are written with the invariant culture;
    /// a field holding a comma, quote or line break is quoted. Files older than
    /// RetentionDaysForCSV are deleted by Master.Scheduler's DeleteFilesJob (it is under LogFilePath).
    /// </remarks>
    public sealed class MachineRecordStore
    {
        private readonly string _folder;
        private readonly string _kind;
        private readonly string[] _columns;
        private readonly object _sync = new object();

        public MachineRecordStore(string rootFolder, string kind, params string[] columns)
        {
            _kind = kind ?? throw new ArgumentNullException(nameof(kind));
            _folder = Path.Combine(rootFolder ?? string.Empty, kind);
            _columns = columns ?? new string[0];
        }

        public string Folder => _folder;

        /// <summary>Appends one record, dated <paramref name="time"/>. Throws when the file cannot be written.</summary>
        public void Append(DateTime time, IReadOnlyList<string> values)
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_folder);
                string file = FileFor(time.Date);
                bool isNew = !File.Exists(file);
                using (var writer = new StreamWriter(file, true, new UTF8Encoding(false)))
                {
                    if (isNew)
                        writer.WriteLine(string.Join(",", new[] { "Time" }.Concat(_columns).Select(Quote)));

                    writer.WriteLine(string.Join(",",
                        new[] { time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) }
                            .Concat(values ?? new string[0]).Select(Quote)));
                }
            }
        }

        /// <summary>Every record between <paramref name="start"/> and <paramref name="end"/>, oldest first: its time and its fields by column.</summary>
        public IReadOnlyList<KeyValuePair<DateTime, Dictionary<string, string>>> Query(DateTime start, DateTime end)
        {
            var rows = new List<KeyValuePair<DateTime, Dictionary<string, string>>>();
            if (end < start)
                return rows;

            lock (_sync)
            {
                for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
                {
                    string file = FileFor(day);
                    if (!File.Exists(file))
                        continue;

                    string[] header = null;
                    foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                    {
                        if (line.Length == 0)
                            continue;

                        List<string> fields = Split(line);
                        if (header == null)
                        {
                            header = fields.ToArray();
                            continue;
                        }

                        DateTime time;
                        if (fields.Count == 0 || !DateTime.TryParseExact(fields[0], "yyyy-MM-dd HH:mm:ss.fff",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                        {
                            continue;
                        }

                        if (time < start || time > end)
                            continue;

                        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 1; i < header.Length && i < fields.Count; i++)
                            values[header[i]] = fields[i];

                        rows.Add(new KeyValuePair<DateTime, Dictionary<string, string>>(time, values));
                    }
                }
            }

            return rows.OrderBy(r => r.Key).ToList();
        }

        private string FileFor(DateTime day)
        {
            return Path.Combine(_folder, _kind + "_" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
        }

        public static string Number(double? value)
        {
            return value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;
        }

        public static double? ParseNumber(Dictionary<string, string> row, string column)
        {
            string raw;
            double value;
            return row.TryGetValue(column, out raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : (double?)null;
        }

        public static string Field(Dictionary<string, string> row, string column)
        {
            string raw;
            return row.TryGetValue(column, out raw) ? raw ?? string.Empty : string.Empty;
        }

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        private static List<string> Split(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
