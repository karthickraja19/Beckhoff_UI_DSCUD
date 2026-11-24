using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// A CSV file with one row per item and one Description_&lt;language&gt; column per
    /// language (Description_English, Description_zh-CN, Description_vi), as
    /// Odyssey.Master_V3's Data\Config.csv.
    ///
    /// Saving rewrites only the description columns. Every other cell of every row is
    /// written back exactly as it was read, so the loaders that read the same file by
    /// column position (Config.csv's I/O list) are not disturbed; missing language
    /// columns are appended at the end of the row.
    /// </summary>
    internal sealed class DescriptionFile
    {
        public const string English = "English";

        private readonly string _keyColumn;
        private readonly string _categoryColumn;
        private readonly string _contextColumn;
        private readonly string[] _languageColumns;
        private readonly string _templateHeader;

        private readonly object _sync = new object();
        private DateTime _loadedStamp;
        private List<string[]> _rows;
        private string[] _header;
        private Dictionary<string, int> _byKey;

        public DescriptionFile(string path, string keyColumn, string categoryColumn, string contextColumn,
            string[] languageColumns, string templateHeader = null)
        {
            Path = path;
            _keyColumn = keyColumn;
            _categoryColumn = categoryColumn;
            _contextColumn = contextColumn;
            _languageColumns = languageColumns;
            _templateHeader = templateHeader;
        }

        public string Path { get; }

        /// <summary>Column name for a culture: "en" -> Description_English, "zh-CN" -> Description_zh-CN.</summary>
        public static string ColumnFor(string culture)
        {
            if (string.IsNullOrWhiteSpace(culture) || culture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "Description_" + English;

            if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "Description_zh-CN";

            if (culture.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
                return "Description_vi";

            return "Description_" + culture;
        }

        /// <summary>Rows as (key, category, context, column -> value).</summary>
        public List<Tuple<string, string, string, Dictionary<string, string>>> ReadAll()
        {
            lock (_sync)
            {
                EnsureLoaded();
                var list = new List<Tuple<string, string, string, Dictionary<string, string>>>();
                int key = Index(_keyColumn), cat = Index(_categoryColumn), ctx = Index(_contextColumn);
                foreach (string[] row in _rows)
                {
                    string k = Cell(row, key);
                    if (k.Length == 0)
                        continue;

                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 0; c < _header.Length; c++)
                    {
                        if (_header[c].StartsWith("Description_", StringComparison.OrdinalIgnoreCase))
                            values[_header[c]] = Cell(row, c);
                    }

                    list.Add(Tuple.Create(k, Cell(row, cat), Cell(row, ctx), values));
                }

                return list;
            }
        }

        /// <summary>The description of <paramref name="key"/> in <paramref name="column"/>; empty when none.</summary>
        public string Get(string key, string column)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            lock (_sync)
            {
                EnsureLoaded();
                int row;
                if (!_byKey.TryGetValue(key, out row))
                    return string.Empty;

                return Cell(_rows[row], Index(column));
            }
        }

        /// <summary>
        /// Writes descriptions. <paramref name="changes"/>: key -> (column -> text). A key not
        /// in the file is added as a new row (spec limits only - the I/O list is the PLC's).
        /// </summary>
        public void Save(IDictionary<string, Dictionary<string, string>> changes, bool allowNewRows)
        {
            lock (_sync)
            {
                Load();

                var header = _header.ToList();
                foreach (string column in _languageColumns)
                {
                    if (!header.Any(h => string.Equals(h, column, StringComparison.OrdinalIgnoreCase)))
                        header.Add(column);
                }
                _header = header.ToArray();

                foreach (var change in changes)
                {
                    int rowIndex;
                    if (!_byKey.TryGetValue(change.Key, out rowIndex))
                    {
                        if (!allowNewRows)
                            continue;

                        var fresh = new string[_header.Length];
                        for (int i = 0; i < fresh.Length; i++) fresh[i] = string.Empty;
                        fresh[Index(_keyColumn)] = change.Key;
                        _rows.Add(fresh);
                        rowIndex = _rows.Count - 1;
                        _byKey[change.Key] = rowIndex;
                    }

                    string[] row = _rows[rowIndex];
                    foreach (var cell in change.Value)
                    {
                        int column = Index(cell.Key);
                        if (column < 0)
                            continue;
                        if (row.Length <= column)
                        {
                            int old = row.Length;
                            Array.Resize(ref row, _header.Length);
                            for (int i = old; i < row.Length; i++) row[i] = string.Empty;
                        }
                        row[column] = Clean(cell.Value);
                    }
                    _rows[rowIndex] = row;
                }

                var text = new StringBuilder();
                text.Append(string.Join(",", _header.Select(Quote))).Append("\r\n");
                foreach (string[] row in _rows)
                    text.Append(string.Join(",", row.Select(Quote))).Append("\r\n");

                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string temp = Path + ".tmp";
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(Path))
                    File.Replace(temp, Path, Path + ".bak");
                else
                    File.Move(temp, Path);

                _loadedStamp = File.GetLastWriteTimeUtc(Path);
            }
        }

        private void EnsureLoaded()
        {
            DateTime stamp = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;
            if (_rows == null || stamp != _loadedStamp)
                Load();
        }

        private void Load()
        {
            _rows = new List<string[]>();
            _byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _header = new string[0];

            if (!File.Exists(Path))
            {
                _header = (_templateHeader ?? _keyColumn).Split(',');
                _loadedStamp = DateTime.MinValue;
                return;
            }

            string[] lines = File.ReadAllLines(Path, Encoding.UTF8);
            _loadedStamp = File.GetLastWriteTimeUtc(Path);
            if (lines.Length == 0)
                return;

            _header = Split(lines[0]).Select(h => h.Trim()).ToArray();
            int key = Index(_keyColumn);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                    continue;

                string[] row = Split(lines[i]);
                _rows.Add(row);
                string k = Cell(row, key);
                if (k.Length > 0 && !_byKey.ContainsKey(k))
                    _byKey.Add(k, _rows.Count - 1);
            }
        }

        private int Index(string column)
        {
            if (string.IsNullOrEmpty(column) || _header == null)
                return -1;

            for (int i = 0; i < _header.Length; i++)
            {
                if (string.Equals(_header[i], column, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static string Cell(string[] row, int index)
        {
            return index >= 0 && index < row.Length ? (row[index] ?? string.Empty).Trim() : string.Empty;
        }

        /// <summary>A description is one line on screen; line breaks would split the CSV row.</summary>
        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string[] Split(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells.ToArray();
        }
    }
}
