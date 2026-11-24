using System;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The running <see cref="ILocalizationService"/> for code that shows text but is not
    /// handed the service - chart series names, the HIVE alarm pop-up. Set once at start-up.
    /// Every call returns its input unchanged until then, and never throws.
    /// </summary>
    public static class UiLanguage
    {
        private static ILocalizationService _service;

        public static ILocalizationService Service
        {
            get { return _service; }
            set
            {
                if (_service != null)
                    _service.LanguageChanged -= OnLanguageChanged;
                _service = value;
                if (_service != null)
                    _service.LanguageChanged += OnLanguageChanged;
            }
        }

        private static IDescriptionService _descriptions;

        /// <summary>The I/O point and spec limit descriptions (Setting &gt; Config Description).</summary>
        public static IDescriptionService Descriptions
        {
            get { return _descriptions; }
            set
            {
                if (_descriptions != null)
                    _descriptions.Changed -= OnDescriptionsChanged;
                _descriptions = value;
                if (_descriptions != null)
                    _descriptions.Changed += OnDescriptionsChanged;
            }
        }

        /// <summary>Raised (UI thread) after a description was saved or the language changed.</summary>
        public static event EventHandler DescriptionsChanged;

        /// <summary>An I/O point's name in the selected language (its description), or the name.</summary>
        public static string Point(string pointName)
        {
            IDescriptionService descriptions = _descriptions;
            if (descriptions == null || string.IsNullOrEmpty(pointName))
                return pointName;
            try { return descriptions.PointName(pointName); }
            catch (Exception) { return pointName; }
        }

        /// <summary>A spec limit parameter's name in the selected language, or the parameter.</summary>
        public static string SpecLimit(string parameter)
        {
            IDescriptionService descriptions = _descriptions;
            if (descriptions == null || string.IsNullOrEmpty(parameter))
                return parameter;
            try { return descriptions.SpecLimitName(parameter); }
            catch (Exception) { return parameter; }
        }

        private static void OnDescriptionsChanged(object sender, EventArgs e)
        {
            DescriptionsChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Raised (on the UI thread) after the language changed or the files were edited.</summary>
        public static event EventHandler Changed;

        public static string Text(string english)
        {
            ILocalizationService service = _service;
            if (service == null || string.IsNullOrEmpty(english))
                return english;
            try { return service.Translate(english); }
            catch (Exception) { return english; }
        }

        /// <summary>Alarm text in the selected language, by its PLC code where known.</summary>
        public static string Alarm(string text, string plcCode = null)
        {
            ILocalizationService service = _service;
            if (service == null || string.IsNullOrEmpty(text))
                return text;
            try { return service.TranslateAlarm(text, plcCode); }
            catch (Exception) { return text; }
        }

        private static void OnLanguageChanged(object sender, EventArgs e)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
