using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nova.Abstractions.Data;

namespace Nova.Wpf.App.Converters
{
    /// <summary>
    /// Product SKU to the station-ID plate fill, per Audio SW DFM v5.4 slides 40 and 42.
    /// </summary>
    /// <remarks>
    /// The colours are resolved from the <c>Audio.Sku.*Brush</c> resources rather than
    /// written as literals here, so the DFM values stay declared in exactly one place
    /// (Nova.Wpf.UI/Styles/AudioTokens.xaml). A <c>Common</c> product has no fill at
    /// all - slide 42 shows the station ID as a plain grey watermark in that case, and
    /// the view swaps to a different element rather than filling this one.
    /// </remarks>
    public sealed class SkuToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProductSku))
                return Brushes.Transparent;

            string key;
            switch ((ProductSku)value)
            {
                case ProductSku.Normal: key = "Audio.Sku.NormalBrush"; break;
                case ProductSku.M: key = "Audio.Sku.MBrush"; break;
                case ProductSku.E: key = "Audio.Sku.EBrush"; break;
                default: return Brushes.Transparent;
            }

            object brush = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Product SKU to the label drawn on the station-ID plate ("Normal(E/M)", "M-SKU",
    /// "E-SKU"), exactly as slide 42 letters them.
    /// </summary>
    public sealed class SkuToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ProductSku))
                return string.Empty;

            switch ((ProductSku)value)
            {
                case ProductSku.Normal: return "Normal(E/M)";
                case ProductSku.M: return "M-SKU";
                case ProductSku.E: return "E-SKU";
                default: return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// A boolean to one of two brushes named by resource key, supplied as
    /// "TrueKey|FalseKey" in the binding's ConverterParameter.
    /// </summary>
    /// <remarks>
    /// Keys rather than brush properties on the converter, so a single shared instance
    /// serves every pass/fail, connected/disconnected and in-spec/out-of-spec pair on
    /// the page without a converter instance per pair - and so the actual colours stay
    /// in the token dictionary where they can be checked against the DFM.
    /// </remarks>
    public sealed class BoolToBrushKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string keys = parameter as string;
            if (string.IsNullOrEmpty(keys))
                return Brushes.Transparent;

            string[] parts = keys.Split('|');
            if (parts.Length != 2)
                return Brushes.Transparent;

            bool flag = value is bool && (bool)value;
            string key = flag ? parts[0] : parts[1];

            object brush = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// A 0..1 ratio to a star <see cref="GridLength"/>, for the Home page's Error
    /// Statistics bars (slide 43).
    /// </summary>
    /// <remarks>
    /// The bars are a two-column Grid - filled part, empty part - rather than a chart,
    /// because the slide puts the value label immediately after the bar's end, which a
    /// chart control positions for you and not where the slide has it. Pass "invert" as
    /// the ConverterParameter for the empty column.
    /// <para>
    /// A zero star length collapses the column entirely, which is correct for a
    /// zero-count category: the bar should vanish, not show a stub.
    /// </para>
    /// </remarks>
    public sealed class RatioToStarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double ratio;
            if (value == null || !double.TryParse(
                    System.Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out ratio))
            {
                ratio = 0d;
            }

            if (ratio < 0d) ratio = 0d;
            if (ratio > 1d) ratio = 1d;

            bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
            return new GridLength(invert ? 1d - ratio : ratio, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// A zero-based rank to its Top 10 downtime colour (Audio SW DFM v5.4 slide 40).
    /// </summary>
    /// <remarks>
    /// Used by the Alarm page's Top 10 table so each row's swatch is the same colour as
    /// its slice in the pie beside it. The pie is painted from the same ordinal table
    /// in <c>AudioChartPalette</c>, so the two stay in step by construction rather than
    /// by both being maintained.
    /// </remarks>
    public sealed class RankToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int rank = 0;
            if (value is int)
                rank = (int)value;

            if (rank < 0)
                rank = 0;

            // Wraps past ten rather than throwing, matching AudioChartPalette.TopN.
            string key = "Audio.Top10.Brush" + ((rank % 10) + 1);

            object brush = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// A Data page health level (Normal, Warning, Critical, Unknown) to green, orange,
    /// red or grey - the Health tab's status colours (S2.4.4.4).
    /// </summary>
    /// <remarks>
    /// Matched on the level's name rather than the ViewModels enum type, and resolved
    /// from the <c>Audio.System.*Brush</c> tokens like the other converters here.
    /// </remarks>
    public sealed class HealthLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key;
            switch (value != null ? value.ToString() : string.Empty)
            {
                case "Normal": key = "Audio.System.GreenBrush"; break;
                case "Warning": key = "Audio.System.OrangeBrush"; break;
                case "Critical": key = "Audio.System.RedBrush"; break;
                default: key = "Audio.System.GrayBrush"; break;
            }

            object brush = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// True to <see cref="Visibility.Visible"/>, false to
    /// <see cref="Visibility.Collapsed"/>; pass "invert" to reverse it.
    /// </summary>
    /// <remarks>
    /// Collapsed rather than Hidden throughout: every use here is a panel that should
    /// give its space back when it is not showing, not one that should leave a hole.
    /// </remarks>
    public sealed class AudioBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;

            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
                flag = !flag;

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
