using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Services;
using Nova.Wpf.UI.Localization;
using Teal.Library.Localization;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Reads and writes the Strings*.resx files for the Localization Editor tab.
    ///
    /// Writes land on the loose files the application already reads, then reload the live
    /// string table and poke every localized binding, so a corrected string appears on the
    /// screen behind the editor immediately — no restart, no rebuild. That is what the
    /// editor exists for: a translator working on the machine, without Visual Studio.
    /// </summary>
    public class WpfLocalizationEditorService : ILocalizationEditorService
    {
        public IReadOnlyList<LanguageOption> EditableLanguages
        {
            get
            {
                return LanguageManager.Available
                    .Select(l => new LanguageOption(l.Culture, l.DisplayName))
                    .ToList();
            }
        }

        public IReadOnlyList<LocalizationEntry> Load(string culture)
        {
            AppLanguage english = LanguageManager.Find(LanguageManager.DefaultCulture);
            AppLanguage target = LanguageManager.Find(culture);

            Dictionary<string, string> defaults = english == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ResxFile.Read(english.FilePath);

            // Editing English itself: both columns are the same file, and the editor shows
            // the master beside the value being changed.
            Dictionary<string, string> translations =
                target == null || (english != null && string.Equals(target.FilePath, english.FilePath, StringComparison.OrdinalIgnoreCase))
                    ? defaults
                    : ResxFile.Read(target.FilePath);

            // Union: a key that exists only in a translation is an orphan from a rename and
            // should be visible so it can be removed, not hidden because English lost it.
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var key in defaults.Keys) keys.Add(key);
            foreach (var key in translations.Keys) keys.Add(key);

            var rows = new List<LocalizationEntry>(keys.Count);
            foreach (string key in keys)
            {
                string defaultText;
                string translated;
                defaults.TryGetValue(key, out defaultText);
                translations.TryGetValue(key, out translated);

                rows.Add(new LocalizationEntry
                {
                    Key = key,
                    DefaultText = defaultText ?? string.Empty,
                    TranslatedText = translated ?? string.Empty
                });
            }

            return rows;
        }

        public void Save(string culture, IEnumerable<LocalizationEntry> entries)
        {
            if (entries == null)
                return;

            AppLanguage english = LanguageManager.Find(LanguageManager.DefaultCulture);
            AppLanguage target = LanguageManager.Find(culture);

            if (english == null)
                throw new InvalidOperationException("Strings.resx was not found, so nothing can be saved.");

            bool editingEnglish = target == null ||
                string.Equals(target.FilePath, english.FilePath, StringComparison.OrdinalIgnoreCase);

            var rows = entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Key)).ToList();

            // Compare against disk and write only what changed, so the resx diff shows the
            // strings a translator actually touched rather than the whole file.
            Dictionary<string, string> onDiskDefaults = ResxFile.Read(english.FilePath);
            var defaultChanges = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                string text = row.DefaultText ?? string.Empty;
                string existing;
                if (!onDiskDefaults.TryGetValue(row.Key, out existing) || !string.Equals(existing, text, StringComparison.Ordinal))
                    defaultChanges[row.Key] = text;
            }

            if (defaultChanges.Count > 0)
                ResxFile.Write(english.FilePath, defaultChanges);

            if (!editingEnglish)
            {
                Dictionary<string, string> onDisk = ResxFile.Read(target.FilePath);
                var changes = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var row in rows)
                {
                    string text = row.TranslatedText ?? string.Empty;
                    string existing;
                    bool present = onDisk.TryGetValue(row.Key, out existing);

                    // A row cleared back to empty is a deliberate "fall back to English",
                    // so remove the entry rather than storing a blank that would render as
                    // an empty label on a machine screen.
                    if (string.IsNullOrEmpty(text))
                    {
                        if (present)
                            ResxFile.Remove(target.FilePath, row.Key);

                        continue;
                    }

                    if (!present || !string.Equals(existing, text, StringComparison.Ordinal))
                        changes[row.Key] = text;
                }

                if (changes.Count > 0)
                    ResxFile.Write(target.FilePath, changes);
            }

            // Re-read what is now on disk and repaint. Without this the editor would save
            // correctly and the screen behind it would still show the old wording.
            LocalizationProvider.Reload();

            // Text the view models resolved themselves (sidebar, tab captions, dialog
            // messages, the Hive popups) is only re-read on a language change, so raise
            // one for the language already selected.
            LanguageManager.Refresh();

            LocalizationSource.Instance.Refresh();
        }
    }
}
