using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPA.Converters
{
    /// <summary>
    /// Конвертер, который преобразует true в Collapsed и false в Visible
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool boolValue ? boolValue ? Visibility.Collapsed : Visibility.Visible : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility != Visibility.Visible;
        }
    }
}