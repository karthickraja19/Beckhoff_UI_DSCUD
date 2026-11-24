using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// One language the operator can choose: its culture name ("en", "zh-CN", "vi") and
    /// the name shown for it.
    /// </summary>
    public sealed class LanguageOption
    {
        public LanguageOption(string culture, string displayName)
        {
            Culture = culture;
            DisplayName = displayName;
        }

        public string Culture { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The application's dual-language service (English, Chinese, Vietnamese).
    ///
    /// Two ways in:
    ///   * by key, for text that lives in Strings*.resx (<see cref="Get(string)"/>, and the
    ///     <c>{loc:Tr Key}</c> markup extension in XAML);
    ///   * by English text, for what the view models build themselves - a status line, a
    ///     dialog message, a tab title (<see cref="Translate"/>, and <c>{loc:TrBinding}</c>
    ///     in XAML). The English stays what the view models compare and store; only what is
    ///     shown is translated.
    ///
    /// Kept in Nova.Abstractions so the netstandard view models can use it without the
    /// .NET Framework resource library.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>The active culture, e.g. "en", "zh-CN" or "vi".</summary>
        string CurrentCulture { get; }

        /// <summary>True when the active language is not English.</summary>
        bool IsTranslated { get; }

        /// <summary>Every language with a Strings*.resx file on disk.</summary>
        IReadOnlyList<LanguageOption> Available { get; }

        string this[string key] { get; }

        /// <summary>Text for <paramref name="key"/> in the active language, English when it has none.</summary>
        string Get(string key);

        /// <summary>Formatted text for <paramref name="key"/>; the resx value is the format string.</summary>
        string Get(string key, params object[] args);

        /// <summary>The English text for <paramref name="key"/>.</summary>
        string GetDefault(string key);

        /// <summary>
        /// <paramref name="english"/> in the active language, looked up by its English text
        /// in the language files. Returned unchanged when the language is English or the
        /// text is not in the files. Never throws.
        /// </summary>
        string Translate(string english);

        /// <summary>
        /// Alarm text (error type, message or detail) in the active language, taken only
        /// from the alarm entries of the language files: Alarm_&lt;PLC code&gt;,
        /// AlarmDesc_&lt;PLC description&gt; and AlarmType_&lt;error type&gt;. No machine
        /// translation. Returned unchanged when the language is English or no entry exists.
        /// </summary>
        string TranslateAlarm(string text, string plcCode);

        /// <summary>Switches language. A culture with no file on disk is ignored.</summary>
        void Apply(string culture);

        /// <summary>Raised after the language changed or the language files were edited.</summary>
        event EventHandler LanguageChanged;
    }
}
