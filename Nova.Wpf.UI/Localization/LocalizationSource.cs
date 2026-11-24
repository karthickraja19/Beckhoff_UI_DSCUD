using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Nova.Abstractions.Services;

namespace Nova.Wpf.UI.Localization
{
    /// <summary>
    /// The binding source every localized string in XAML points at.
    ///
    /// <c>Text="{loc:Tr Some_Key}"</c> becomes a one-way binding onto the indexer below, and
    /// <c>Text="{loc:TrBinding Status}"</c> a binding onto the view model plus
    /// <see cref="Version"/>. When the language changes this raises PropertyChanged for
    /// both, and WPF re-reads every one of those bindings: the screen in front of the
    /// operator changes language without being rebuilt.
    /// </summary>
    public sealed class LocalizationSource : INotifyPropertyChanged
    {
        private static readonly LocalizationSource _instance = new LocalizationSource();

        private ILocalizationService _service;
        private int _version;

        private LocalizationSource()
        {
        }

        /// <summary>The single source shared by every binding in the application.</summary>
        public static LocalizationSource Instance
        {
            get { return _instance; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Localized text for <paramref name="key"/>. Before <see cref="Attach"/> (in the
        /// XAML designer) the key itself, so an unwired binding is obvious.
        /// </summary>
        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key))
                    return string.Empty;

                ILocalizationService service = _service;
                return service == null ? key : service.Get(key);
            }
        }

        /// <summary>Changes on every language switch or editor save; bound beside view-model text.</summary>
        public int Version
        {
            get { return _version; }
        }

        /// <summary><paramref name="english"/> in the active language; unchanged before Attach.</summary>
        public string Translate(string english)
        {
            ILocalizationService service = _service;
            if (service == null || string.IsNullOrEmpty(english))
                return english;

            try
            {
                return service.Translate(english);
            }
            catch (Exception)
            {
                return english;
            }
        }

        /// <summary>Alarm text in the active language, from the alarm entries of the language files.</summary>
        public string TranslateAlarm(string text)
        {
            ILocalizationService service = _service;
            if (service == null || string.IsNullOrEmpty(text))
                return text;

            try
            {
                return service.TranslateAlarm(text, null);
            }
            catch (Exception)
            {
                return text;
            }
        }

        /// <summary>
        /// A one-way binding to <paramref name="path"/> whose text is shown in the selected
        /// language, for columns and cells built in code. Same as {loc:TrBinding path}.
        /// </summary>
        public static BindingBase TranslatedBinding(string path)
        {
            var multi = new MultiBinding { Mode = BindingMode.OneWay, Converter = FixedTextConverter.Instance };
            multi.Bindings.Add(string.IsNullOrEmpty(path) ? new Binding { Mode = BindingMode.OneWay } : new Binding(path) { Mode = BindingMode.OneWay });
            multi.Bindings.Add(new Binding("Version") { Source = _instance, Mode = BindingMode.OneWay });
            return multi;
        }

        private sealed class FixedTextConverter : IMultiValueConverter
        {
            internal static readonly FixedTextConverter Instance = new FixedTextConverter();

            public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                object value = values == null || values.Length == 0 ? null : values[0];
                if (value == DependencyProperty.UnsetValue)
                    return DependencyProperty.UnsetValue;

                var text = value as string;
                if (text != null)
                    return _instance.Translate(text);

                return value == null ? null : System.Convert.ToString(value, culture);
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Wires the source to the running localization service, once at start-up.</summary>
        public void Attach(ILocalizationService service)
        {
            if (service == null)
                throw new ArgumentNullException("service");

            if (_service != null)
                _service.LanguageChanged -= OnLanguageChanged;

            _service = service;
            _service.LanguageChanged += OnLanguageChanged;

            Refresh();
        }

        /// <summary>
        /// Tells every localized binding to re-read its value. Public so the Localization
        /// Editor can call it after a save.
        /// </summary>
        public void Refresh()
        {
            Action raise = () =>
            {
                _version++;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler == null)
                    return;

                // Binding.IndexerName ("Item[]") invalidates every indexer binding at once.
                handler(this, new PropertyChangedEventArgs(Binding.IndexerName));
                handler(this, new PropertyChangedEventArgs("Version"));
            };

            // WPF wants change notifications on the dispatcher.
            Application application = Application.Current;
            if (application != null && !application.Dispatcher.CheckAccess())
                application.Dispatcher.Invoke(raise);
            else
                raise();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Refresh();
        }
    }
}
