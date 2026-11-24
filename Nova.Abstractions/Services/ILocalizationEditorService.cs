using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// One row of the Localization Editor: a key, its English master text, and its text in
    /// the language being edited.
    /// </summary>
    public sealed class LocalizationEntry
    {
        public string Key { get; set; }

        /// <summary>English master, from Strings.resx. Editable — English is data too.</summary>
        public string DefaultText { get; set; }

        /// <summary>Text in the language being edited. Empty means "falls back to English".</summary>
        public string TranslatedText { get; set; }
    }

    /// <summary>
    /// Reads and writes the translation files behind the Localization Editor screen.
    ///
    /// Kept as an abstraction because <c>Nova.ViewModels</c> is .NET Standard and cannot
    /// reach the resource library, which is .NET Framework — the same split that keeps
    /// <see cref="ILocalizationService"/> platform-free.
    ///
    /// Writing goes straight to the loose resx files the application reads at runtime, so
    /// a correction made on the machine takes effect without a rebuild. That is the whole
    /// reason the resources are not compiled in.
    /// </summary>
    public interface ILocalizationEditorService
    {
        /// <summary>
        /// Languages that can be edited — every discovered language including English.
        /// </summary>
        IReadOnlyList<LanguageOption> EditableLanguages { get; }

        /// <summary>
        /// Every key known to the application, with its English and its
        /// <paramref name="culture"/> text. Keys present only in a translation are
        /// included, so an orphan can be seen and cleaned up rather than sitting invisible.
        /// </summary>
        IReadOnlyList<LocalizationEntry> Load(string culture);

        /// <summary>
        /// Writes the entries back: English into Strings.resx, the translation into that
        /// culture's file. Only rows whose text actually differs from what is on disk are
        /// written, so the file stays a readable diff.
        ///
        /// Reloads the running string table afterwards, so the screens update immediately.
        /// Throws on I/O failure — a translator must be told when a save did not happen.
        /// </summary>
        void Save(string culture, IEnumerable<LocalizationEntry> entries);
    }
}
