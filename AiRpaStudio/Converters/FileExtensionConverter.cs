using System;
using System.Globalization;
using System.Windows.Data;

namespace RPA.Converters
{
    public class FileExtensionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string fileName && parameter is string extension && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}