using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPA.Converters
{
    /// <summary>
    /// Given an object o and a type t, return Visible if o is of type t, else return Collapsed.
    /// </summary>
    public class TypeVisibilityCheck : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ((Type)parameter).IsInstanceOfType(value) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
