using System;
using System.Globalization;
using System.Windows.Data;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Конвертер для расчета ширины колонки сообщений
    /// Вычисляет доступную ширину минус ширина колонок времени и уровня
    /// </summary>
    public class MessageColumnWidthConverter : IValueConverter
    {
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static readonly MessageColumnWidthConverter Instance = new MessageColumnWidthConverter();

        /// <summary>
        /// Ширина колонки времени
        /// </summary>
        private const double TimeColumnWidth = 100;

        /// <summary>
        /// Ширина колонки уровня
        /// </summary>
        private const double LevelColumnWidth = 80;

        /// <summary>
        /// Запас для скроллбара и отступов
        /// </summary>
        private const double ScrollbarMargin = 20;

        /// <summary>
        /// Преобразование ширины ListView в ширину колонки сообщений
        /// </summary>
        /// <param name="value">Ширина ListView</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр преобразования</param>
        /// <param name="culture">Культура</param>
        /// <returns>Рассчитанная ширина колонки сообщений</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double listViewWidth && listViewWidth > 0)
            {
                // Вычисляем доступную ширину для колонки сообщений
                double messageColumnWidth = listViewWidth - TimeColumnWidth - LevelColumnWidth - ScrollbarMargin;
                
                // Минимальная ширина колонки сообщений
                return Math.Max(messageColumnWidth, 200);
            }

            // Ширина по умолчанию
            return 400.0;
        }

        /// <summary>
        /// Обратное преобразование не поддерживается
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("Обратное преобразование не поддерживается");
        }
    }
}