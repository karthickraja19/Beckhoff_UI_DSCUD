using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Nova.Wpf.UI.Localization
{
    /// <summary>
    /// A <c>{Binding}</c> whose text is shown in the selected language:
    ///
    /// <code>
    ///   &lt;TextBlock Text="{loc:TrBinding StatusText}"/&gt;
    ///   &lt;TextBlock Text="{loc:TrBinding Count, StringFormat='{}{0} units'}"/&gt;
    ///   &lt;TextBlock Text="{loc:TrBinding}"/&gt;   (the item itself, e.g. a tab title)
    /// </code>
    ///
    /// The view models produce English - it is also what they compare, store and send to
    /// MES and HIVE - so nothing upstream changes. This binds the value together with
    /// <see cref="LocalizationSource.Version"/> and translates the English at display
    /// time, so the text also follows a language switch while it is on screen.
    ///
    /// Only for one-way display properties (Text of a TextBlock, Content, Header, ToolTip).
    /// Never for a TextBox or anything two-way.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public class TrBindingExtension : MarkupExtension
    {
        public TrBindingExtension()
        {
        }

        public TrBindingExtension(string path)
        {
            Path = path;
        }

        [ConstructorArgument("path")]
        public string Path { get; set; }

        public string ElementName { get; set; }

        public RelativeSource RelativeSource { get; set; }

        public object Source { get; set; }

        public IValueConverter Converter { get; set; }

        public object ConverterParameter { get; set; }

        public string StringFormat { get; set; }

        public object FallbackValue { get; set; } = DependencyProperty.UnsetValue;

        public object TargetNullValue { get; set; } = DependencyProperty.UnsetValue;

        /// <summary>Accepted for a drop-in swap with Binding; display bindings are always one-way.</summary>
        public BindingMode Mode { get; set; }

        public UpdateSourceTrigger UpdateSourceTrigger { get; set; }

        public bool IsAsync { get; set; }

        /// <summary>
        /// True for alarm text (error type, message, detail): it is then taken from the
        /// description of its error code in the selected language, never translated word
        /// by word.
        /// </summary>
        public bool Alarm { get; set; }

        /// <summary>
        /// True for chart Series / XAxes / YAxes: the series names and axis names and labels
        /// (what the legend and the chart tooltip show) are shown in the selected language.
        /// </summary>
        public bool Chart { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var value = string.IsNullOrEmpty(Path) ? new Binding() : new Binding(Path);
            value.Mode = BindingMode.OneWay;
            value.IsAsync = IsAsync;
            if (ElementName != null) value.ElementName = ElementName;
            if (RelativeSource != null) value.RelativeSource = RelativeSource;
            if (Source != null) value.Source = Source;
            if (Converter != null)
            {
                value.Converter = Converter;
                value.ConverterParameter = ConverterParameter;
            }

            var multi = new MultiBinding
            {
                Mode = BindingMode.OneWay,
                Converter = Chart ? TranslateConverter.ChartInstance : Alarm ? TranslateConverter.AlarmInstance : TranslateConverter.Instance,
                ConverterParameter = StringFormat
            };
            if (FallbackValue != DependencyProperty.UnsetValue) multi.FallbackValue = FallbackValue;
            if (TargetNullValue != DependencyProperty.UnsetValue) multi.TargetNullValue = TargetNullValue;

            multi.Bindings.Add(value);
            multi.Bindings.Add(new Binding("Version") { Source = LocalizationSource.Instance, Mode = BindingMode.OneWay });

            return multi.ProvideValue(serviceProvider);
        }

        /// <summary>
        /// values[0] is the view-model value, values[1] the language version. The format
        /// (ConverterParameter) is applied after the value is translated, and is itself
        /// translated, so "{0} units" reads in the selected language too.
        /// </summary>
        private sealed class TranslateConverter : IMultiValueConverter
        {
            internal static readonly TranslateConverter Instance = new TranslateConverter(false);
            internal static readonly TranslateConverter AlarmInstance = new TranslateConverter(true);
            internal static readonly TranslateConverter ChartInstance = new TranslateConverter(true, true);

            // The English each chart object was built with, so a second language change
            // translates from English again rather than from the previous translation.
            private static readonly ConditionalWeakTable<object, Dictionary<string, object>> Originals =
                new ConditionalWeakTable<object, Dictionary<string, object>>();

            private readonly bool _alarm;
            private readonly bool _chart;

            private TranslateConverter(bool alarm, bool chart = false)
            {
                _alarm = alarm;
                _chart = chart;
            }

            private static object TranslateChart(object value)
            {
                var items = value as IEnumerable;
                if (items == null || value is string)
                    return value;

                LocalizationSource source = LocalizationSource.Instance;
                foreach (object item in items)
                {
                    if (item == null)
                        continue;

                    try
                    {
                        Dictionary<string, object> originals = Originals.GetValue(item, _ => new Dictionary<string, object>());
                        Type type = item.GetType();

                        PropertyInfo name = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(p => p.Name == "Name" && p.PropertyType == typeof(string) && p.CanRead && p.CanWrite);
                        if (name != null)
                        {
                            object original;
                            if (!originals.TryGetValue("Name", out original))
                                originals["Name"] = original = name.GetValue(item, null);
                            var text = original as string;
                            if (!string.IsNullOrEmpty(text))
                                name.SetValue(item, source.TranslateAlarm(text), null);
                        }

                        PropertyInfo labels = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(p => p.Name == "Labels" && p.CanRead && p.CanWrite
                                                 && p.PropertyType.IsAssignableFrom(typeof(string[])));
                        if (labels != null)
                        {
                            object original;
                            if (!originals.TryGetValue("Labels", out original))
                                originals["Labels"] = original = labels.GetValue(item, null);
                            var list = original as IEnumerable<string>;
                            if (list != null)
                                labels.SetValue(item, list.Select(l => string.IsNullOrEmpty(l) ? l : source.Translate(l)).ToArray(), null);
                        }
                    }
                    catch (Exception)
                    {
                        // A chart that cannot be relabelled keeps its English.
                    }
                }

                return value;
            }

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                object value = values == null || values.Length == 0 ? null : values[0];
                if (value == DependencyProperty.UnsetValue || value == BindingOperations.DisconnectedSource)
                    return DependencyProperty.UnsetValue;

                LocalizationSource source = LocalizationSource.Instance;

                if (_chart)
                    return TranslateChart(value);

                var text = value as string;
                if (text != null)
                    value = _alarm ? source.TranslateAlarm(text) : source.Translate(text);

                string format = parameter as string;
                if (!string.IsNullOrEmpty(format))
                {
                    if (format.IndexOf('{') < 0)
                        format = "{0:" + format + "}";

                    try
                    {
                        return string.Format(culture, source.Translate(format), value);
                    }
                    catch (FormatException)
                    {
                        return string.Format(culture, format, value);
                    }
                }

                if (value == null)
                    return null;

                // A MultiBinding does no type conversion of its own.
                if (targetType == typeof(string) && !(value is string))
                {
                    var formattable = value as IFormattable;
                    return formattable != null ? formattable.ToString(null, culture) : value.ToString();
                }

                return value;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
