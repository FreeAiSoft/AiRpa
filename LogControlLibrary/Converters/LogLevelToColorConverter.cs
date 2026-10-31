using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogControlLibrary.Models;

namespace LogControlLibrary.Converters
{
    /// <summary>
    /// Уровень логирования → цвет текста колонки «Уровень».
    /// <para>
    /// Цвета уровней — семантика (Trace/Debug/Info/Warning/Error/Fatal), а не акцент интерфейса,
    /// но они обязаны читаться и в тёмной, и в светлой теме студии (docs\Vs2026_Theme_Plan.md, §8 п.8, F-07).
    /// Поэтому здесь средние тона: ни чистого белого (не виден на светлом фоне), ни чистого чёрного
    /// (не виден на тёмном).
    /// </para>
    /// </summary>
    public class LogLevelToColorConverter : IValueConverter
    {
        /// <summary>Trace — нейтральный серый.</summary>
        public Brush TraceBrush { get; set; } = Frozen(Color.FromRgb(0x8C, 0x8C, 0x8C));

        /// <summary>Debug — приглушённый синий.</summary>
        public Brush DebugBrush { get; set; } = Frozen(Color.FromRgb(0x5B, 0x9B, 0xD5));

        /// <summary>Info — приглушённый зелёный.</summary>
        public Brush InfoBrush { get; set; } = Frozen(Color.FromRgb(0x4E, 0x9A, 0x45));

        /// <summary>Warning — приглушённый янтарный.</summary>
        public Brush WarningBrush { get; set; } = Frozen(Color.FromRgb(0xC0, 0x8A, 0x00));

        /// <summary>Error — приглушённый красный.</summary>
        public Brush ErrorBrush { get; set; } = Frozen(Color.FromRgb(0xD1, 0x4B, 0x4B));

        /// <summary>Fatal — тёмный красный, но не чёрный.</summary>
        public Brush FatalBrush { get; set; } = Frozen(Color.FromRgb(0xA6, 0x3A, 0x3A));

        /// <summary>Значение по умолчанию — приглушённый серый.</summary>
        public Brush DefaultBrush { get; set; } = Frozen(Color.FromRgb(0x9D, 0xA0, 0xA6));

        /// <summary>Преобразование уровня логирования в цветовую кисть.</summary>
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
            throw new NotSupportedException("Обратное преобразование не поддерживается для LogLevelToColorConverter");
        }

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
