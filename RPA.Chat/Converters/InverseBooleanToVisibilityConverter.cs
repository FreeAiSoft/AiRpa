using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPA.Chat.Converters
{
    /// <summary>Inverts a boolean for visibility: true → Collapsed, false → Visible.</summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible ? false : true;
        }
    }
}
