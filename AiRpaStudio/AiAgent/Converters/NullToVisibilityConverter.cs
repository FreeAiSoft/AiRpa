using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Конвертер null в Visibility
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Если параметр "Inverse", показываем когда null, скрываем когда не null
            bool inverse = parameter != null && parameter.ToString() == "Inverse";

            bool isNull = value == null;

            if (inverse)
            {
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                return isNull ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}