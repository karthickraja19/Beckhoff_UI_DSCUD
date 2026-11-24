using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// The Config Description tab's two sources, and the names the screens show from them:
    ///   * I/O Points  - Data\Config.csv, the same file the I/O list is loaded from. The
    ///                   I/O Diagnostics tab shows each input and output by its description
    ///                   in the selected language (as Odyssey.Master_V3).
    ///   * Spec Limits - Data\Settings\SpecLimitDescriptions.csv. The Config page's Spec
    ///                   Limits panel shows each parameter by its description.
    /// A point or parameter with no description in the selected language shows its English
    /// description, and with none at all its own name - what the screens showed before.
    /// </summary>
    public sealed class WpfDescriptionService : IDescriptionService
    {
        public const string IoPoints = "I/O Points";
        public const string SpecLimits = "Spec Limits";

        private static readonly string[] Languages =
            { "Description_English", "Description_zh-CN", "Description_vi" };

        private readonly DescriptionFile _io;
        private readonly DescriptionFile _spec;
        private readonly Func<IEnumerable<KeyValuePair<string, string>>> _specParameters;
        private readonly ILocalizationService _localization;

        /// <param name="specParameters">Every spec limit parameter and the limit set it belongs to.</param>
        public WpfDescriptionService(string dataDirectory, ILocalizationService localization,
            Func<IEnumerable<KeyValuePair<string, string>>> specParameters)
        {
            _localization = localization;
            _specParameters = specParameters;
            _io = new DescriptionFile(Path.Combine(dataDirectory, "Config.csv"),
                "Name", "Content", "IO Name", Languages);
            _spec = new DescriptionFile(Path.Combine(dataDirectory, "Settings", "SpecLimitDescriptions.csv"),
                "Parameter", "Limits", null, Languages,
                "Parameter,Limits,Description_English,Description_zh-CN,Description_vi");

            if (_localization != null)
                _localization.LanguageChanged += (sender, e) => Raise();
        }

        public event EventHandler Changed;

        public IReadOnlyList<string> Sources { get; } = new[] { IoPoints, SpecLimits };

        public string PointName(string pointName)
        {
            return Describe(_io, pointName);
        }

        public string SpecLimitName(string parameter)
        {
            return Describe(_spec, parameter);
        }

        public string FilePathOf(string source)
        {
            return FileOf(source).Path;
        }

        public IReadOnlyList<DescriptionEntry> Load(string source, string culture)
        {
            DescriptionFile file = FileOf(source);
            string englishColumn = DescriptionFile.ColumnFor("en");
            string column = DescriptionFile.ColumnFor(culture);

            var rows = new List<DescriptionEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in file.ReadAll())
            {
                if (!seen.Add(row.Item1))
                    continue;

                rows.Add(new DescriptionEntry
                {
                    Key = row.Item1,
                    Category = row.Item2,
                    Context = row.Item3,
                    English = Value(row.Item4, englishColumn),
                    Translation = Value(row.Item4, column),
                });
            }

            // Spec limits: every parameter the limit files have, described yet or not.
            if (file == _spec && _specParameters != null)
            {
                foreach (var parameter in _specParameters())
                {
                    if (string.IsNullOrWhiteSpace(parameter.Key) || !seen.Add(parameter.Key))
                        continue;

                    rows.Add(new DescriptionEntry { Key = parameter.Key, Category = parameter.Value, English = string.Empty, Translation = string.Empty });
                }
            }

            return rows;
        }

        public void Save(string source, string culture, IEnumerable<DescriptionEntry> entries)
        {
            if (entries == null)
                return;

            DescriptionFile file = FileOf(source);
            string englishColumn = DescriptionFile.ColumnFor("en");
            string column = DescriptionFile.ColumnFor(culture);
            bool editingEnglish = string.Equals(column, englishColumn, StringComparison.OrdinalIgnoreCase);

            var changes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (DescriptionEntry entry in entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Key)))
            {
                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.Equals(file.Get(entry.Key, englishColumn), entry.English ?? string.Empty, StringComparison.Ordinal))
                    cells[englishColumn] = entry.English ?? string.Empty;
                if (!editingEnglish && !string.Equals(file.Get(entry.Key, column), entry.Translation ?? string.Empty, StringComparison.Ordinal))
                    cells[column] = entry.Translation ?? string.Empty;

                if (cells.Count > 0)
                {
                    if (file == _spec)
                        cells["Limits"] = entry.Category ?? string.Empty;
                    changes[entry.Key] = cells;
                }
            }

            if (changes.Count == 0)
                return;

            file.Save(changes, allowNewRows: file == _spec);
            Raise();
        }

        private string Describe(DescriptionFile file, string key)
        {
            if (string.IsNullOrEmpty(key))
                return key ?? string.Empty;

            try
            {
                string culture = _localization == null ? "en" : _localization.CurrentCulture;
                string text = file.Get(key, DescriptionFile.ColumnFor(culture));
                if (string.IsNullOrWhiteSpace(text))
                    text = file.Get(key, DescriptionFile.ColumnFor("en"));
                return string.IsNullOrWhiteSpace(text) ? key : text;
            }
            catch (Exception)
            {
                // A description file problem must never blank a screen: show the name.
                return key;
            }
        }

        private DescriptionFile FileOf(string source)
        {
            return string.Equals(source, SpecLimits, StringComparison.OrdinalIgnoreCase) ? _spec : _io;
        }

        private static string Value(Dictionary<string, string> values, string column)
        {
            string value;
            return values.TryGetValue(column, out value) ? value ?? string.Empty : string.Empty;
        }

        private void Raise()
        {
            EventHandler handler = Changed;
            if (handler == null)
                return;

            Application application = Application.Current;
            if (application != null && !application.Dispatcher.CheckAccess())
                application.Dispatcher.BeginInvoke(new Action(() => handler(this, EventArgs.Empty)));
            else
                handler(this, EventArgs.Empty);
        }
    }
}
