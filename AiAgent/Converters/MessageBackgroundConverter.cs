using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AiAgent.Converters
{
    /// <summary>
    /// Конвертер для цвета фона сообщений в зависимости от роли
    /// </summary>
    public class MessageBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                // Сообщения пользователя - светло-синий
                if (role == "user")
                    return new SolidColorBrush(Color.FromRgb(220, 237, 255));
                
                // Сообщения AI - светло-серый
                if (role == "assistant")
                    return new SolidColorBrush(Color.FromRgb(240, 240, 240));
            }

            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}