using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiAgent.Converters
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
                
                if (inverse)
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                else
                    return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool inverse = parameter != null && parameter.ToString() == "Inverse";
                
                if (inverse)
                    return visibility != Visibility.Visible;
                else
                    return visibility == Visibility.Visible;
            }

            return false;
        }
    }
}