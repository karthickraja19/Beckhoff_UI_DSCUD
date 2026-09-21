using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nova.Wpf.App.Converters
{
    public class Boolean2VisibilityReValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isCollapsed = value is bool && (bool)value;
            return isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
