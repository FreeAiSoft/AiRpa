using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogControlLibrary.Models;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Уровень логирования → фон строки под курсором.
    /// <para>
    /// Как и фон строки, подсветка — полупрозрачная накладка, поэтому она одинаково выглядит на тёмном
    /// и на светлом фоне (docs\Vs2026_Theme_Plan.md, §8 п.8, F-07).
    /// </para>
    /// </summary>
    public class LogLevelToHoverConverter : IValueConverter
    {
        private const byte HoverAlpha = 0x28;

        /// <summary>Trace — без подсветки: строки этого уровня не получают фона ни в покое, ни под курсором.</summary>
        public Brush TraceBrush { get; set; } = Frozen(Colors.Transparent);

        /// <summary>Debug — синяя подсветка.</summary>
        public Brush DebugBrush { get; set; } = Frozen(Overlay(0x5B, 0x9B, 0xD5));

        /// <summary>Info — зелёная подсветка.</summary>
        public Brush InfoBrush { get; set; } = Frozen(Overlay(0x4E, 0x9A, 0x45));

        /// <summary>Warning — янтарная подсветка.</summary>
        public Brush WarningBrush { get; set; } = Frozen(Overlay(0xC0, 0x8A, 0x00));

        /// <summary>Error — красная подсветка.</summary>
        public Brush ErrorBrush { get; set; } = Frozen(Overlay(0xD1, 0x4B, 0x4B));

        /// <summary>Fatal — тёмно-красная подсветка.</summary>
        public Brush FatalBrush { get; set; } = Frozen(Overlay(0xA6, 0x3A, 0x3A));

        /// <summary>Значение по умолчанию — нейтральная подсветка.</summary>
        public Brush DefaultBrush { get; set; } = Frozen(Overlay(0x8C, 0x8C, 0x8C));

        /// <summary>Преобразование уровня логирования в кисть подсветки.</summary>
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

        /// <summary>Обратное преобразование (не поддерживается).</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Обратное преобразование не поддерживается для LogLevelToHoverConverter");
        }

        private static Color Overlay(byte r, byte g, byte b)
        {
            return Color.FromArgb(HoverAlpha, r, g, b);
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
