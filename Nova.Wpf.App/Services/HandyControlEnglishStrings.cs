using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Makes HandyControl's own UI strings render in English.
    ///
    /// HandyControl 3.5.1 ships no localised satellite assemblies — verified against the
    /// package: 50 entries, 18 lib folders, not one HandyControl.resources.dll. Its single
    /// embedded resource, HandyControl.Properties.Langs.Lang.resources, is the *neutral*
    /// fallback and is written in Chinese, so asking for en, en-GB or en-US all resolve to
    /// Chinese. Setting Lang.Culture therefore cannot help: there is nothing to fall back to.
    ///
    /// This is what leaves 确定 / 上午 / 下午 on the alarm filter's DateTimePicker popup
    /// after the calendar grid itself is already English — the grid comes from WPF's
    /// Calendar (fixed by <see cref="CalendarLanguageFix"/>), while those three come from
    /// HandyControl's own resource table.
    ///
    /// The fix substitutes the ResourceManager that table is read through. Only keys listed
    /// below are answered; anything else falls through to HandyControl's original manager,
    /// so an unmapped or newly added key keeps working rather than turning into a blank.
    ///
    /// Caveat worth knowing: this reaches into a private static field of a third-party
    /// library. It is guarded and non-fatal, but a HandyControl upgrade that renames that
    /// field will silently stop it working — the strings would simply revert to Chinese.
    /// </summary>
    internal static class HandyControlEnglishStrings
    {
        /// <summary>
        /// English for HandyControl's resource keys. Deliberately covers the whole table
        /// rather than only the three strings seen so far, so any other HandyControl
        /// control surfaced later is English too.
        /// </summary>
        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "All", "All" },
                { "Am", "AM" },
                { "Cancel", "Cancel" },
                { "Clear", "Clear" },
                { "Close", "Close" },
                { "CloseAll", "Close all" },
                { "CloseOther", "Close others" },
                { "Confirm", "OK" },
                { "ErrorImgPath", "Invalid image path" },
                { "ErrorImgSize", "Invalid image size" },
                { "Find", "Find" },
                { "FormatError", "Invalid format" },
                { "Interval10m", "Every 10 minutes" },
                { "Interval1h", "Every hour" },
                { "Interval1m", "Every minute" },
                { "Interval2h", "Every 2 hours" },
                { "Interval30m", "Every 30 minutes" },
                { "Interval30s", "Every 30 seconds" },
                { "Interval5m", "Every 5 minutes" },
                { "IsNecessary", "Required" },
                { "Jump", "Go" },
                { "Miscellaneous", "Miscellaneous" },
                { "NextPage", "Next page" },
                { "No", "No" },
                { "NoData", "No data" },
                { "OutOfRange", "Out of range" },
                { "PageMode", "Page mode" },
                { "Pm", "PM" },
                { "PngImg", "PNG image" },
                { "PreviousPage", "Previous page" },
                { "ScrollMode", "Scroll mode" },
                { "Tip", "Tip" },
                { "TooLarge", "Too large" },
                { "TwoPageMode", "Two-page mode" },
                { "Unknown", "Unknown" },
                { "UnknownSize", "Unknown size" },
                { "Yes", "Yes" },
                { "ZoomIn", "Zoom in" },
                { "ZoomOut", "Zoom out" },
                // LangComment is the resx designer's own doc-comment template, not UI text,
                // and is left to the original deliberately.
            };

        /// <summary>
        /// Installs the substitution. Must run before any HandyControl control is created,
        /// so the first read of a string already goes through it.
        /// </summary>
        public static void Apply()
        {
            try
            {
                var langType = typeof(HandyControl.Controls.Window).Assembly
                    .GetType("HandyControl.Properties.Langs.Lang");
                if (langType == null)
                    return;

                // Read through the public property first: its getter creates the original
                // manager on demand, so this both fetches and guarantees a real fallback.
                var property = langType.GetProperty("ResourceManager",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var original = property == null ? null : property.GetValue(null, null) as ResourceManager;

                var field = langType.GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                    return;

                field.SetValue(null, new EnglishResourceManager(original));
            }
            catch (Exception ex)
            {
                // Cosmetic only — a Chinese confirm button must not stop the machine.
                System.Diagnostics.Debug.WriteLine("HandyControl string localisation failed: " + ex.Message);
            }
        }

        private sealed class EnglishResourceManager : ResourceManager
        {
            private readonly ResourceManager _fallback;

            internal EnglishResourceManager(ResourceManager fallback)
            {
                _fallback = fallback;
            }

            public override string GetString(string name)
            {
                return GetString(name, null);
            }

            public override string GetString(string name, CultureInfo culture)
            {
                string value;
                if (name != null && English.TryGetValue(name, out value))
                    return value;

                return _fallback == null ? null : _fallback.GetString(name, culture);
            }
        }
    }
}
