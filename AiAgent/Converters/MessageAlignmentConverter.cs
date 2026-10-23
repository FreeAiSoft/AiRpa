using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AiAgent.Converters
{
    /// <summary>
    /// Конвертер для выравнивания сообщений в зависимости от роли
    /// </summary>
    public class MessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                // Сообщения пользователя выравниваем по правому краю
                if (role == "user")
                    return HorizontalAlignment.Right;
                
                // Сообщения AI выравниваем по левому краю
                if (role == "assistant")
                    return HorizontalAlignment.Left;
            }

            return HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}