using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Конвертер bool в Visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Если параметр "Inverse", инвертируем логику
                bool inverse = parameter != null && parameter.ToString() == "Inverse";

                return inverse ? boolValue ? Visibility.Collapsed : Visibility.Visible : (object)(boolValue ? Visibility.Visible : Visibility.Collapsed);
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool inverse = parameter != null && parameter.ToString() == "Inverse";

                return inverse ? visibility != Visibility.Visible : (object)(visibility == Visibility.Visible);
            }

            return false;
        }
    }
}
