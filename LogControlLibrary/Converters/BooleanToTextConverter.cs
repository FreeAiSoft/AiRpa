using System;
using System.Globalization;
using System.Windows.Data;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Конвертер для преобразования Boolean значений в текст
    /// </summary>
    public class BooleanToTextConverter : IValueConverter
    {
        /// <summary>
        /// Текст для значения True (по умолчанию "Да")
        /// </summary>
        public string TrueText { get; set; } = "Да";

        /// <summary>
        /// Текст для значения False (по умолчанию "Нет")
        /// </summary>
        public string FalseText { get; set; } = "Нет";

        /// <summary>
        /// Преобразование Boolean в текст
        /// </summary>
        /// <param name="value">Boolean значение</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр (может содержать текст для True|False разделенный |)</param>
        /// <param name="culture">Культура</param>
        /// <returns>Текстовое представление</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Проверяем, переданы ли кастомные тексты через параметр
                if (parameter is string paramText && !string.IsNullOrEmpty(paramText))
                {
                    var parts = paramText.Split('|');
                    if (parts.Length >= 2)
                    {
                        return boolValue ? parts[0] : parts[1];
                    }
                    else if (parts.Length == 1)
                    {
                        return boolValue ? parts[0] : FalseText;
                    }
                }

                // Используем значения по умолчанию
                return boolValue ? TrueText : FalseText;
            }

            return value?.ToString() ?? FalseText;
        }

        /// <summary>
        /// Обратное преобразование (не поддерживается)
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("BooleanToTextConverter не поддерживает обратное преобразование");
        }
    }
}