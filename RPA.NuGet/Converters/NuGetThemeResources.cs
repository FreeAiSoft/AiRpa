using System.Windows;
using System.Windows.Media;

namespace RPA.NuGet.Converters
{
    /// <summary>
    /// Мост от панели NuGet к кистям и шрифтам студии для кода (не XAML).
    /// <para>
    /// Палитра студии объявлена в <c>AiRpaStudio</c> (<c>Helpers\StudioBrushes</c>), а <c>RPA.NuGet</c>
    /// на неё не ссылается — поэтому ключи <c>Studio.*</c> резолвятся из
    /// <see cref="Application.Current"/> в момент отрисовки. Ссылки динамические: если ключ не найден
    /// (дизайнер, тесты без <c>App</c>), берётся нейтральный фолбэк, а не исключение.
    /// </para>
    /// </summary>
    public static class NuGetThemeResources
    {
        public const string ForegroundKey = "Studio.Foreground";
        public const string ForegroundMutedKey = "Studio.ForegroundMuted";
        public const string SurfaceAltKey = "Studio.SurfaceAlt";
        public const string SeparatorKey = "Studio.Separator";
        public const string SeparatorStrongKey = "Studio.SeparatorStrong";
        public const string FontFamilyKey = "Studio.FontFamily";
        public const string CodeFontFamilyKey = "Studio.CodeFontFamily";

        private static readonly Brush FallbackForeground = CreateFrozen(Color.FromRgb(0xF1, 0xF1, 0xF1));
        private static readonly Brush FallbackMuted = CreateFrozen(Color.FromRgb(0x9D, 0xA0, 0xA6));
        private static readonly Brush FallbackSurfaceAlt = CreateFrozen(Color.FromRgb(0x2E, 0x2E, 0x2F));
        private static readonly Brush FallbackSeparator = CreateFrozen(Color.FromRgb(0x37, 0x37, 0x37));
        private static readonly Brush FallbackSeparatorStrong = CreateFrozen(Color.FromRgb(0x48, 0x48, 0x4F));

        /// <summary>Кисть по ключу палитры студии (фолбэк — нейтральный серый).</summary>
        public static Brush Brush(string key)
        {
            return Resolve(key) as Brush ?? Fallback(key);
        }

        /// <summary>Шрифт интерфейса студии (фолбэк — системный).</summary>
        public static FontFamily Font()
        {
            return Resolve(FontFamilyKey) as FontFamily ?? new FontFamily("Segoe UI");
        }

        /// <summary>Моноширинный шрифт для блоков кода.</summary>
        public static FontFamily CodeFont()
        {
            return Resolve(CodeFontFamilyKey) as FontFamily
                   ?? Resolve(FontFamilyKey) as FontFamily
                   ?? new FontFamily("Consolas");
        }

        private static object Resolve(string key)
        {
            Application application = Application.Current;
            if (application == null)
                return null;

            try
            {
                return application.TryFindResource(key);
            }
            catch (ResourceReferenceKeyNotFoundException)
            {
                return null;
            }
        }

        private static Brush Fallback(string key)
        {
            switch (key)
            {
                case ForegroundMutedKey:
                    return FallbackMuted;
                case SurfaceAltKey:
                    return FallbackSurfaceAlt;
                case SeparatorKey:
                    return FallbackSeparator;
                case SeparatorStrongKey:
                    return FallbackSeparatorStrong;
                default:
                    return FallbackForeground;
            }
        }

        private static Brush CreateFrozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
