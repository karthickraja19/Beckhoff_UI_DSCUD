using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nova.Abstractions.Services;
using Teal.Library.Localization;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// English / Chinese / Vietnamese for the WPF HMI, over Teal.Library.Localization.
    ///
    /// The language is the station profile's "Language" (Setting page &gt; General), so it is
    /// chosen and saved where it always was and survives a restart. A saved change applies
    /// at once: the profile raises ProfileChanged and every localized binding re-reads.
    ///
    /// The strings live in Localization\Strings.resx (English), Strings.zh-CN.resx and
    /// Strings.vi.resx beside the exe - loose files, so the Localization Editor tab can
    /// correct a translation on the machine.
    /// </summary>
    public sealed class WpfLocalizationService : ILocalizationService
    {
        private readonly IStationProfileService _profile;

        public WpfLocalizationService(IStationProfileService profile)
        {
            _profile = profile;

            try
            {
                LanguageManager.Initialize(ToCulture(profile == null || profile.Current == null ? null : profile.Current.Language));
            }
            catch (Exception ex)
            {
                // No language files: the screens show their English.
                System.Diagnostics.Debug.WriteLine("Language initialisation failed: " + ex.Message);
            }

            LanguageManager.LanguageChanged += OnLibraryLanguageChanged;

            if (_profile != null)
                _profile.ProfileChanged += OnProfileChanged;
        }

        public event EventHandler LanguageChanged;

        public string CurrentCulture
        {
            get { return LanguageManager.CurrentCulture; }
        }

        public bool IsTranslated
        {
            get { return LanguageManager.IsTranslated; }
        }

        public IReadOnlyList<LanguageOption> Available
        {
            get
            {
                // Named as the Setting page lists them ("English", "简体中文", "Tiếng Việt"),
                // so the top-bar dropdown and the Setting page read the same.
                IReadOnlyList<string> named = _profile == null ? null : _profile.Languages;
                List<LanguageOption> found = LanguageManager.Available
                    .Select(l => new LanguageOption(l.Culture, NameFor(l.Culture, named) ?? l.DisplayName))
                    .ToList();

                // No language files beside the exe (not copied by the build): still offer
                // the station profile's languages, so the dropdown is never empty. The
                // start-up warning names the folder the files were expected in.
                if (found.Count == 0 && named != null)
                {
                    found = named
                        .Select(n => new LanguageOption(ToCulture(n), n))
                        .GroupBy(o => o.Culture, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();
                }

                return found;
            }
        }

        public string this[string key]
        {
            get { return Get(key); }
        }

        public string Get(string key)
        {
            return LocalizationProvider.Get(key);
        }

        public string Get(string key, params object[] args)
        {
            return LocalizationProvider.Get(key, args);
        }

        public string GetDefault(string key)
        {
            return LocalizationProvider.GetDefault(key);
        }

        public string Translate(string english)
        {
            return UiTextTranslator.Translate(english);
        }

        public string TranslateAlarm(string text, string plcCode)
        {
            return AlarmTextLocalizer.Translate(text, plcCode);
        }

        /// <summary>
        /// Switches the whole application to <paramref name="culture"/> at once and keeps
        /// the choice in the station profile, so it survives a restart and the Setting
        /// page's Language field shows the same. Used by the top-bar language dropdown.
        /// </summary>
        public void Apply(string culture)
        {
            string target = ToCulture(culture);
            if (!string.Equals(target, LanguageManager.CurrentCulture, StringComparison.OrdinalIgnoreCase))
                LanguageManager.SetLanguage(target);

            Persist(target);
        }

        private void Persist(string culture)
        {
            if (_profile == null || _profile.Current == null)
                return;

            // Already saved: nothing to write (this is also what stops the save from
            // coming back round through ProfileChanged).
            if (string.Equals(ToCulture(_profile.Current.Language), culture, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                StationProfile draft = _profile.Current.Clone();
                draft.Language = NameFor(culture, _profile.Languages) ?? culture;

                string error;
                if (!_profile.Save(draft, out error))
                    System.Diagnostics.Debug.WriteLine("The language could not be saved: " + error);
            }
            catch (Exception ex)
            {
                // The screen has already changed language; only the remembering failed.
                System.Diagnostics.Debug.WriteLine("The language could not be saved: " + ex.Message);
            }
        }

        /// <summary>The station-profile name of a culture, e.g. "zh-CN" -> "简体中文".</summary>
        private static string NameFor(string culture, IReadOnlyList<string> names)
        {
            if (names == null)
                return null;

            foreach (string name in names)
            {
                if (string.Equals(ToCulture(name), culture, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            return null;
        }

        /// <summary>
        /// The station profile stores the language as it is offered in StationOptions.json
        /// ("English", "简体中文", "Tiếng Việt"); the library wants a culture name.
        /// A culture name is accepted as well.
        /// </summary>
        public static string ToCulture(string language)
        {
            string value = (language ?? string.Empty).Trim();
            if (value.Length == 0)
                return LanguageManager.DefaultCulture;

            string lower = value.ToLowerInvariant();

            if (lower == "english" || lower.StartsWith("en"))
                return "en";

            if (value.IndexOf("中文", StringComparison.Ordinal) >= 0 || value.IndexOf("汉语", StringComparison.Ordinal) >= 0
                || lower.StartsWith("chinese") || lower.StartsWith("zh"))
                return "zh-CN";

            if (lower.StartsWith("tiếng việt") || lower.StartsWith("tieng viet") || lower.StartsWith("vietnam") || lower.StartsWith("vi"))
                return "vi";

            return value;
        }

        private void OnProfileChanged(object sender, EventArgs e)
        {
            StationProfile current = _profile == null ? null : _profile.Current;
            if (current == null)
                return;

            RunOnUi(() =>
            {
                // Saved on the Setting page: follow it (nothing to save back).
                string target = ToCulture(current.Language);
                if (!string.Equals(target, LanguageManager.CurrentCulture, StringComparison.OrdinalIgnoreCase))
                    LanguageManager.SetLanguage(target);
            });
        }

        private void OnLibraryLanguageChanged(object sender, EventArgs e)
        {
            UiTextTranslator.Reset();

            EventHandler handler = LanguageChanged;
            if (handler != null)
                RunOnUi(() => handler(this, EventArgs.Empty));
        }

        private static void RunOnUi(Action action)
        {
            Application application = Application.Current;
            if (application != null && !application.Dispatcher.CheckAccess())
                application.Dispatcher.BeginInvoke(action);
            else
                action();
        }
    }
}
