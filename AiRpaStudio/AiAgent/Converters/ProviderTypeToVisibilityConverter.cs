using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Конвертер для отображения/скрытия контролов в зависимости от выбранного провайдера
    /// </summary>
    public class ProviderTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ProviderType selectedProvider && parameter is string targetProviderString)
            {
                if (Enum.TryParse<ProviderType>(targetProviderString, out ProviderType targetProvider))
                {
                    return selectedProvider == targetProvider ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}