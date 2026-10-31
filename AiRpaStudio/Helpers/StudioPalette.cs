using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Единая палитра студии в стиле Visual Studio 2026 (правила и токены — docs\Vs2026_Theme_Plan.md, §1-§2).
    /// <para>
    /// Что гарантирует палитра:
    /// один фон (<see cref="StudioThemeColors.Surface"/>) для окна, панелей, заголовков, вкладок, контента
    /// документов и поверхности дизайнера; различие зон передаётся только hairline-разделителем и яркостью
    /// (SurfaceAlt → Hover → Selected); акцентных цветов нет — единственное исключение красный hover кнопки
    /// закрытия; ровно две палитры (тёмная и светлая), третьей «смешанной» нет.
    /// </para>
    /// <para>
    /// Кисти из этих значений создаются только кодом (см. <see cref="StudioBrushes"/> и
    /// <see cref="DockingThemeBrushes"/>): объявлять алиасы в XAML через
    /// <c>&lt;DynamicResource x:Key="..." ResourceKey="..."/&gt;</c> запрещено — в словарь попадает
    /// <c>ResourceReferenceExpression</c> и WPF падает с <c>XamlParseException</c> на сеттерах стилей.
    /// </para>
    /// </summary>
    public static class StudioPalette
    {
        // Имена токенов (см. таблицу §2 плана).
        public const string SurfaceToken = "Surface";
        public const string SurfaceAltToken = "SurfaceAlt";
        public const string HoverToken = "Hover";
        public const string SelectedToken = "Selected";
        public const string SeparatorToken = "Separator";
        public const string SeparatorStrongToken = "SeparatorStrong";
        public const string ForegroundToken = "Foreground";
        public const string ForegroundMutedToken = "ForegroundMuted";
        public const string CloseHoverBackgroundToken = "CloseHoverBackground";
        public const string CloseHoverForegroundToken = "CloseHoverForeground";
        public const string OverlayFillToken = "OverlayFill";
        public const string OverlayStrokeToken = "OverlayStroke";

        /// <summary>Все токены, которые обязана содержать каждая палитра.</summary>
        public static readonly IReadOnlyList<string> TokenNames = new[]
        {
            SurfaceToken,
            SurfaceAltToken,
            HoverToken,
            SelectedToken,
            SeparatorToken,
            SeparatorStrongToken,
            ForegroundToken,
            ForegroundMutedToken,
            CloseHoverBackgroundToken,
            CloseHoverForegroundToken,
            OverlayFillToken,
            OverlayStrokeToken
        };

        /// <summary>
        /// Токены фона и состояний: строго монохромные (max(R,G,B) - min(R,G,B) ≤ 6)
        /// и различаются только яркостью — это и есть механизм передачи состояния без акцента.
        /// </summary>
        public static readonly IReadOnlyList<string> NeutralTokenNames = new[]
        {
            SurfaceToken,
            SurfaceAltToken,
            HoverToken,
            SelectedToken,
            SeparatorToken
        };

        /// <summary>
        /// Токены текста и границ: допускается лёгкий холодный сдвиг (≤ 12), потому что «чистый» серый
        /// на мелком тексте читается хуже.
        /// </summary>
        public static readonly IReadOnlyList<string> TextTokenNames = new[]
        {
            ForegroundToken,
            ForegroundMutedToken,
            SeparatorStrongToken
        };

        // Скругления (правило 3 раздела §1 плана).
        public const double WindowCornerRadius = 8d;
        public const double TabCornerRadius = 4d;
        public const double ButtonCornerRadius = 4d;
        public const double MenuCornerRadius = 6d;

        /// <summary>Прозрачность заливки оверлея перетаскивания: Surface @15%.</summary>
        public const byte OverlayFillAlpha = 0x26;

        private static readonly StudioThemeColors DarkPalette = new StudioThemeColors(
            isDark: true,
            surface: Rgb(0x27, 0x27, 0x27),
            surfaceAlt: Rgb(0x2E, 0x2E, 0x2F),
            hover: Rgb(0x33, 0x36, 0x38),
            selected: Rgb(0x40, 0x40, 0x46),
            separator: Rgb(0x37, 0x37, 0x37),
            separatorStrong: Rgb(0x48, 0x48, 0x4F),
            foreground: Rgb(0xF1, 0xF1, 0xF1),
            foregroundMuted: Rgb(0x9D, 0xA0, 0xA6),
            closeHoverBackground: Rgb(0xE8, 0x11, 0x23),
            closeHoverForeground: Rgb(0xFF, 0xFF, 0xFF));

        private static readonly StudioThemeColors LightPalette = new StudioThemeColors(
            isDark: false,
            surface: Rgb(0xFF, 0xFF, 0xFF),
            surfaceAlt: Rgb(0xF3, 0xF3, 0xF3),
            hover: Rgb(0xEA, 0xEA, 0xEA),
            selected: Rgb(0xE4, 0xE4, 0xE4),
            separator: Rgb(0xE4, 0xE4, 0xE4),
            separatorStrong: Rgb(0xCF, 0xCF, 0xCF),
            foreground: Rgb(0x1F, 0x1F, 0x1F),
            foregroundMuted: Rgb(0x5F, 0x63, 0x68),
            closeHoverBackground: Rgb(0xE8, 0x11, 0x23),
            closeHoverForeground: Rgb(0xFF, 0xFF, 0xFF));

        /// <summary>Тёмная палитра (тема по умолчанию).</summary>
        public static StudioThemeColors Dark => DarkPalette;

        /// <summary>Светлая палитра.</summary>
        public static StudioThemeColors Light => LightPalette;

        /// <summary>Палитра по признаку темы.</summary>
        public static StudioThemeColors For(bool dark)
        {
            return dark ? DarkPalette : LightPalette;
        }

        /// <summary>Палитра по словарю ресурсов (тема определяется по яркости фона MaterialDesign).</summary>
        public static StudioThemeColors Detect(ResourceDictionary resources)
        {
            return For(DetectDark(resources));
        }

        /// <summary>
        /// Определяет тему по яркости фона в словаре ресурсов. Если фон найти не удалось,
        /// считаем тему тёмной: студия стартует в dark (M-04 плана).
        /// </summary>
        public static bool DetectDark(ResourceDictionary resources)
        {
            if (resources != null)
            {
                foreach (string key in BackgroundKeys)
                {
                    if (TryGetColor(resources, key, out Color background))
                    {
                        return Luminance(background) < 0.5;
                    }
                }
            }

            return true;
        }

        private static readonly string[] BackgroundKeys =
        {
            "MaterialDesign.Brush.Background",
            "MaterialDesign.Brush.Card.Background",
            "MaterialDesign.Brush.ToolBar.Background"
        };

        /// <summary>Воспринимаемая яркость 0..1.</summary>
        public static double Luminance(Color color)
        {
            return (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255d;
        }

        /// <summary>Монохромность: разница между максимальным и минимальным каналом.</summary>
        public static int Chroma(Color color)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            return max - min;
        }

        /// <summary>Тот же цвет с другой альфой (используется для заливки оверлея).</summary>
        public static Color WithAlpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        /// <summary>Читает цвет из кисти словаря, не разворачивая отложенные ссылки.</summary>
        public static bool TryGetColor(ResourceDictionary resources, string key, out Color color)
        {
            color = default;
            if (resources == null || key == null || !resources.Contains(key))
            {
                return false;
            }

            if (resources[key] is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }

            return false;
        }

        private static Color Rgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }
    }

    /// <summary>Набор токенов одной темы (тёмной или светлой).</summary>
    public sealed class StudioThemeColors
    {
        private readonly Dictionary<string, Color> _tokens;

        internal StudioThemeColors(
            bool isDark,
            Color surface,
            Color surfaceAlt,
            Color hover,
            Color selected,
            Color separator,
            Color separatorStrong,
            Color foreground,
            Color foregroundMuted,
            Color closeHoverBackground,
            Color closeHoverForeground)
        {
            IsDark = isDark;
            _tokens = new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                [StudioPalette.SurfaceToken] = surface,
                [StudioPalette.SurfaceAltToken] = surfaceAlt,
                [StudioPalette.HoverToken] = hover,
                [StudioPalette.SelectedToken] = selected,
                [StudioPalette.SeparatorToken] = separator,
                [StudioPalette.SeparatorStrongToken] = separatorStrong,
                [StudioPalette.ForegroundToken] = foreground,
                [StudioPalette.ForegroundMutedToken] = foregroundMuted,
                [StudioPalette.CloseHoverBackgroundToken] = closeHoverBackground,
                [StudioPalette.CloseHoverForegroundToken] = closeHoverForeground,
                // Заливка оверлея перетаскивания — тот же Surface с прозрачностью 15%.
                [StudioPalette.OverlayFillToken] = StudioPalette.WithAlpha(surface, StudioPalette.OverlayFillAlpha),
                [StudioPalette.OverlayStrokeToken] = separatorStrong
            };
        }

        /// <summary>Признак тёмной темы.</summary>
        public bool IsDark { get; }

        /// <summary>Единственный фон всего: окно, панели, заголовки, вкладки, контент документов, дизайнер.</summary>
        public Color Surface => Get(StudioPalette.SurfaceToken);

        /// <summary>Фон неактивной вкладки — первый шаг яркости от <see cref="Surface"/>.</summary>
        public Color SurfaceAlt => Get(StudioPalette.SurfaceAltToken);

        /// <summary>Фон под курсором.</summary>
        public Color Hover => Get(StudioPalette.HoverToken);

        /// <summary>Выбранное состояние.</summary>
        public Color Selected => Get(StudioPalette.SelectedToken);

        /// <summary>Hairline-разделитель 1px между зонами.</summary>
        public Color Separator => Get(StudioPalette.SeparatorToken);

        /// <summary>Усиленный hairline: внешняя граница плавающего окна, hover ресайзера.</summary>
        public Color SeparatorStrong => Get(StudioPalette.SeparatorStrongToken);

        /// <summary>Основной текст.</summary>
        public Color Foreground => Get(StudioPalette.ForegroundToken);

        /// <summary>Неактивные вкладки и подписи.</summary>
        public Color ForegroundMuted => Get(StudioPalette.ForegroundMutedToken);

        /// <summary>Фон кнопки закрытия под курсором (единственный не-нейтральный токен, системная семантика).</summary>
        public Color CloseHoverBackground => Get(StudioPalette.CloseHoverBackgroundToken);

        /// <summary>Текст кнопки закрытия под курсором.</summary>
        public Color CloseHoverForeground => Get(StudioPalette.CloseHoverForegroundToken);

        /// <summary>Заливка рамки при перетаскивании вкладки: Surface @15%.</summary>
        public Color OverlayFill => Get(StudioPalette.OverlayFillToken);

        /// <summary>Обводка рамки при перетаскивании вкладки.</summary>
        public Color OverlayStroke => Get(StudioPalette.OverlayStrokeToken);

        /// <summary>Все токены палитры (имя → цвет).</summary>
        public IReadOnlyDictionary<string, Color> Tokens => _tokens;

        /// <summary>Цвет токена по имени.</summary>
        public Color Get(string tokenName)
        {
            if (tokenName == null || !_tokens.TryGetValue(tokenName, out Color color))
            {
                throw new ArgumentOutOfRangeException(nameof(tokenName), tokenName, "неизвестный токен палитры студии");
            }

            return color;
        }

        /// <summary>Пробует получить цвет токена по имени.</summary>
        public bool TryGet(string tokenName, out Color color)
        {
            color = default;
            return tokenName != null && _tokens.TryGetValue(tokenName, out color);
        }
    }
}
