using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Forces every calendar popup in the app to render in English.
    ///
    /// HandyControl's theme leaves <see cref="FrameworkElement.Language"/> on
    /// <see cref="Calendar"/> set to zh-cn. WPF's CalendarItem builds its day-title row
    /// and its year/month header from that Language — not from CultureInfo.CurrentCulture
    /// — so every date picker in the app rendered a Chinese calendar (年/月 header,
    /// 一二三四五六日 day names) on an en-IN machine. Proven by rendering a Calendar with
    /// and without HandyControl's theme merged: Language came out zh-cn with it and en-gb
    /// without, while CurrentCulture was en-GB in both cases.
    ///
    /// That is also why pinning the thread culture never helped, and why the week still
    /// started on Monday: FirstDayOfWeek does come from the culture, so the Chinese day
    /// names were simply re-ordered by an English first-day.
    ///
    /// A Style setter outranks the inherited default that App.ApplyUiCulture establishes
    /// via LanguageProperty.OverrideMetadata, so the override has to be a Style setter too.
    /// </summary>
    internal static class CalendarLanguageFix
    {
        private static readonly XmlLanguage English = XmlLanguage.GetLanguage("en-GB");

        /// <summary>
        /// Installs the override. Safe to call repeatedly — and it must be re-called after
        /// a theme change, because the styles below inherit from whichever skin's implicit
        /// style is in effect at the time, and swapping the skin replaces that.
        /// </summary>
        public static void Apply()
        {
            var app = Application.Current;
            if (app == null)
                return;

            try
            {
                // Inherit the skin's own Calendar style so its template and colours are
                // untouched; only Language is overridden. A null base is acceptable — it
                // just means the WPF default, which is already English.
                var calendarStyle = new Style(typeof(Calendar), FindImplicitStyle(app, typeof(Calendar)));
                calendarStyle.Setters.Add(new Setter(FrameworkElement.LanguageProperty, English));
                app.Resources[typeof(Calendar)] = calendarStyle;

                // A DatePicker builds its popup calendar from its CalendarStyle property,
                // which the skin may set explicitly — in that case the implicit style above
                // never reaches it. Point it at the corrected style as well, and set
                // Language on the picker itself so its text box formats in English too.
                var datePickerStyle = new Style(typeof(DatePicker), FindImplicitStyle(app, typeof(DatePicker)));
                datePickerStyle.Setters.Add(new Setter(FrameworkElement.LanguageProperty, English));
                datePickerStyle.Setters.Add(new Setter(DatePicker.CalendarStyleProperty, calendarStyle));
                app.Resources[typeof(DatePicker)] = datePickerStyle;
            }
            catch (Exception ex)
            {
                // Cosmetic: a machine that starts with a Chinese calendar still runs.
                System.Diagnostics.Debug.WriteLine("Calendar language fix failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Finds the implicit style for <paramref name="key"/> in the merged dictionaries
        /// only. Deliberately does not look at Application.Resources' own entries: after
        /// the first call those hold the styles produced here, and basing a style on
        /// itself throws.
        /// </summary>
        private static Style FindImplicitStyle(Application app, Type key)
        {
            foreach (var dictionary in app.Resources.MergedDictionaries)
            {
                var found = Lookup(dictionary, key);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Style Lookup(ResourceDictionary dictionary, Type key)
        {
            if (dictionary.Contains(key))
                return dictionary[key] as Style;

            foreach (var child in dictionary.MergedDictionaries)
            {
                var found = Lookup(child, key);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
