using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogControlLibrary.Models;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Конвертер для преобразования уровня логирования в фоновый цвет
    /// </summary>
    public class LogLevelToBackgroundConverter : IValueConverter
    {
        /// <summary>
        /// Фоновый цвет для уровня Trace (прозрачный - без фона)
        /// </summary>
        public Brush TraceBrush { get; set; } = Brushes.Transparent;

        /// <summary>
        /// Фоновый цвет для уровня Debug (очень тусклый синий)
        /// </summary>
        public Brush DebugBrush { get; set; } = new SolidColorBrush(Color.FromArgb(20, 65, 105, 225));

        /// <summary>
        /// Фоновый цвет для уровня Info (очень тусклый зеленый)
        /// </summary>
        public Brush InfoBrush { get; set; } = new SolidColorBrush(Color.FromArgb(20, 34, 139, 34));

        /// <summary>
        /// Фоновый цвет для уровня Warning (очень тусклый оранжевый)
        /// </summary>
        public Brush WarningBrush { get; set; } = new SolidColorBrush(Color.FromArgb(20, 255, 140, 0));

        /// <summary>
        /// Фоновый цвет для уровня Error (очень тусклый красный)
        /// </summary>
        public Brush ErrorBrush { get; set; } = new SolidColorBrush(Color.FromArgb(20, 220, 20, 60));

        /// <summary>
        /// Фоновый цвет для уровня Fatal (очень тусклый темно-красный)
        /// </summary>
        public Brush FatalBrush { get; set; } = new SolidColorBrush(Color.FromArgb(20, 139, 0, 0));

        /// <summary>
        /// Фоновый цвет по умолчанию (прозрачный)
        /// </summary>
        public Brush DefaultBrush { get; set; } = Brushes.Transparent;

        /// <summary>
        /// Преобразование уровня логирования в фоновую кисть
        /// </summary>
        /// <param name="value">Значение для преобразования (LogLevel)</param>
        /// <param name="targetType">Целевой тип</param>
        /// <param name="parameter">Параметр преобразования</param>
        /// <param name="culture">Культура</param>
        /// <returns>Фоновая кисть</returns>
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
            throw new NotSupportedException("Обратное преобразование не поддерживается для LogLevelToBackgroundConverter");
        }
    }
}