using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogControlLibrary.Models;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Конвертер для преобразования уровня логирования в цвет
    /// </summary>
    public class LogLevelToColorConverter : IValueConverter
    {
        /// <summary>
        /// Цвет для уровня Trace (светло-серый)
        /// </summary>
        public Brush TraceBrush { get; set; } = new SolidColorBrush(Color.FromRgb(128, 128, 128));

        /// <summary>
        /// Цвет для уровня Debug (синий)
        /// </summary>
        public Brush DebugBrush { get; set; } = new SolidColorBrush(Color.FromRgb(65, 105, 225));

        /// <summary>
        /// Цвет для уровня Info (зеленый)
        /// </summary>
        public Brush InfoBrush { get; set; } = new SolidColorBrush(Color.FromRgb(34, 139, 34));

        /// <summary>
        /// Цвет для уровня Warning (оранжевый)
        /// </summary>
        public Brush WarningBrush { get; set; } = new SolidColorBrush(Color.FromRgb(255, 140, 0));

        /// <summary>
        /// Цвет для уровня Error (красный)
        /// </summary>
        public Brush ErrorBrush { get; set; } = new SolidColorBrush(Color.FromRgb(220, 20, 60));

        /// <summary>
        /// Цвет для уровня Fatal (темно-красный)
        /// </summary>
        public Brush FatalBrush { get; set; } = new SolidColorBrush(Color.FromRgb(139, 0, 0));

        /// <summary>
        /// Цвет по умолчанию (черный)
        /// </summary>
        public Brush DefaultBrush { get; set; } = new SolidColorBrush(Colors.Black);

        /// <summary>
        /// Преобразование уровня логирования в цветовую кисть
        /// </summary>
        /// <param name="value">Значение для преобразования (LogLevel)</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр преобразования</param>
        /// <param name="culture">Культура</param>
        /// <returns>Цветовая кисть</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LogLevel logLevel)
            {
                switch (logLevel)
                {
                    case LogLevel.Trace:
                        return TraceBrush;
                    case LogLevel.Debug:
                        return DebugBrush;
                    case LogLevel.Info:
                        return InfoBrush;
                    case LogLevel.Warning:
                        return WarningBrush;
                    case LogLevel.Error:
                        return ErrorBrush;
                    case LogLevel.Fatal:
                        return FatalBrush;
                    default:
                        return DefaultBrush;
                }
            }

            return DefaultBrush;
        }

        /// <summary>
        /// Обратное преобразование (не поддерживается)
        /// </summary>
        /// <param name="value">Значение для обратного преобразования</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр преобразования</param>
        /// <param name="culture">Культура</param>
        /// <returns>Исключение</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Обратное преобразование не поддерживается для LogLevelToColorConverter");
        }
    }
}