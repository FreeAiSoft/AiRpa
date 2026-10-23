using System;
using System.Globalization;
using System.Windows.Data;

namespace RPA.Converters
{
    public class FileExtensionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string fileName && parameter is string extension)
            {
                return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}