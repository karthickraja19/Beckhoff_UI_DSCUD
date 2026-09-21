using System;
using System.Globalization;
using System.Windows.Data;

namespace Nova.Wpf.App.Converters
{
    public class Boolean2DoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isCollapsed = value is bool && (bool)value;
            string paramStr = parameter as string;

            double collapsedVal = 76;
            double expandedVal = 240;

            if (!string.IsNullOrEmpty(paramStr))
            {
                string[] parts = paramStr.Split(':');
                if (parts.Length == 2)
                {
                    double.TryParse(parts[0], out collapsedVal);
                    double.TryParse(parts[1], out expandedVal);
                }
            }

            return isCollapsed ? collapsedVal : expandedVal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
