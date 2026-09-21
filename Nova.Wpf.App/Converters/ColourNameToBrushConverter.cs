using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nova.Wpf.App.Converters
{
    /// <summary>
    /// Converts a colour NAME held in data (e.g. the Vision Settings "Colour" column:
    /// "White", "Black", "Green") into a swatch brush. The colour is the datum itself,
    /// not themed styling; unknown names fall back to Transparent.
    /// </summary>
    public sealed class ColourNameToBrushConverter : IValueConverter
    {
        // Lamp-colour names used in data that WPF's ColorConverter does not know.
        private static readonly System.Collections.Generic.Dictionary<string, Color> KnownNames =
            new System.Collections.Generic.Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "Amber", Color.FromRgb(0xFF, 0xBF, 0x00) },
            };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var name = value as string;
            if (string.IsNullOrWhiteSpace(name))
                return Brushes.Transparent;

            if (KnownNames.TryGetValue(name.Trim(), out var known))
            {
                var knownBrush = new SolidColorBrush(known);
                knownBrush.Freeze();
                return knownBrush;
            }

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(name.Trim());
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch (FormatException)
            {
                return Brushes.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
