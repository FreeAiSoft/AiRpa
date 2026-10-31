using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogControlLibrary.Models;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Уровень логирования → фон строки.
    /// <para>
    /// Фон — всегда полупрозрачная накладка на фон панели (<c>Studio.Surface</c>), а не сплошной светлый
    /// цвет: иначе в тёмной теме строки становятся светлыми островами
    /// (docs\Vs2026_Theme_Plan.md, §8 п.8, F-07).
    /// </para>
    /// </summary>
    public class LogLevelToBackgroundConverter : IValueConverter
    {
        private const byte BackgroundAlpha = 0x18;

        /// <summary>Trace — без фона.</summary>
        public Brush TraceBrush { get; set; } = Frozen(Colors.Transparent);

        /// <summary>Debug — едва заметный синий.</summary>
        public Brush DebugBrush { get; set; } = Frozen(Overlay(0x5B, 0x9B, 0xD5));

        /// <summary>Info — едва заметный зелёный.</summary>
        public Brush InfoBrush { get; set; } = Frozen(Overlay(0x4E, 0x9A, 0x45));

        /// <summary>Warning — едва заметный янтарный.</summary>
        public Brush WarningBrush { get; set; } = Frozen(Overlay(0xC0, 0x8A, 0x00));

        /// <summary>Error — едва заметный красный.</summary>
        public Brush ErrorBrush { get; set; } = Frozen(Overlay(0xD1, 0x4B, 0x4B));

        /// <summary>Fatal — едва заметный тёмно-красный.</summary>
        public Brush FatalBrush { get; set; } = Frozen(Overlay(0xA6, 0x3A, 0x3A));

        /// <summary>Значение по умолчанию — без фона.</summary>
        public Brush DefaultBrush { get; set; } = Frozen(Colors.Transparent);

        /// <summary>Преобразование уровня логирования в фоновую кисть.</summary>
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
            throw new NotSupportedException("Обратное преобразование не поддерживается для LogLevelToBackgroundConverter");
        }

        private static Color Overlay(byte r, byte g, byte b)
        {
            return Color.FromArgb(BackgroundAlpha, r, g, b);
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
