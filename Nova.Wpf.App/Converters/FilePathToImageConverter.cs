using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Nova.Wpf.App.Converters
{
    /// <summary>
    /// An image file path to a bitmap, read into memory at once so the vision software can
    /// keep writing and deleting its files. ConverterParameter is the decode width in pixels
    /// (e.g. 640 for a tile); none decodes the full image.
    /// </summary>
    public sealed class FilePathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(path, UriKind.Absolute);

                int width;
                if (int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out width) && width > 0)
                    image.DecodePixelWidth = width;

                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                // Half-written or not an image: no frame rather than a crash.
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
