using System;
using System.Globalization;
using System.Windows.Data;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Конвертер для преобразования DateTime в отформатированную строку
    /// </summary>
    public class DateTimeToStringConverter : IValueConverter
    {
        /// <summary>
        /// Формат по умолчанию для отображения времени
        /// </summary>
        public string DateTimeFormat { get; set; } = "HH:mm:ss.fff";

        /// <summary>
        /// Преобразование DateTime в строку
        /// </summary>
        /// <param name="value">Значение для преобразования (DateTime)</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр преобразования (формат даты)</param>
        /// <param name="culture">Культура</param>
        /// <returns>Отформатированная строка даты</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                string format = parameter as string ?? DateTimeFormat;
                return dateTime.ToString(format, culture);
            }

            return string.Empty;
        }

        /// <summary>
        /// Обратное преобразование строки в DateTime
        /// </summary>
        /// <param name="value">Значение для обратного преобразования</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр преобразования</param>
        /// <param name="culture">Культура</param>
        /// <returns>DateTime или исключение</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && !string.IsNullOrEmpty(stringValue))
            {
                string format = parameter as string ?? DateTimeFormat;
                if (DateTime.TryParseExact(stringValue, format, culture, DateTimeStyles.None, out DateTime result))
                {
                    return result;
                }
            }

            throw new FormatException($"Невозможно преобразовать '{value}' в DateTime");
        }
    }
}