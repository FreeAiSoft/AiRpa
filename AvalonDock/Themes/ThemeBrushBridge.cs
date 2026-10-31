using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace AvalonDock.Themes
{
    /// <summary>
    /// Мост кистей палитры в изолированные ресурсы окон AvalonDock.
    /// <para>
    /// <see cref="DockingManager"/> отдаёт тему в <c>Resources.MergedDictionaries</c> каждого
    /// <see cref="AvalonDock.Controls.LayoutFloatingWindowControl"/>,
    /// <see cref="AvalonDock.Controls.NavigatorWindow"/> и <see cref="AvalonDock.Controls.OverlayWindow"/>,
    /// поэтому их <c>Resources</c> не видят <c>Application.Resources</c>, куда студия кладёт свои кисти.
    /// Без моста откреплённая панель, переключатель документов (Ctrl+Tab) и оверлей перетаскивания
    /// остаются светлыми в тёмной теме.
    /// </para>
    /// <para>
    /// Мост копирует значения из <c>Application.Resources</c> (их пишет студия, см.
    /// <c>AppSettings.ReapplyDockManagerResources</c>) и, если ключа там нет, использует нейтральные
    /// fallback-значения — совпадают с палитрой студии по умолчанию (тёмная тема). Никакого акцента
    /// здесь нет по определению.
    /// </para>
    /// </summary>
    public static class ThemeBrushBridge
    {
        /// <summary>Единственный фон: панели, заголовки, вкладки, контент.</summary>
        public const string SurfaceKey = "AvalonDock_Surface";

        /// <summary>Фон неактивной вкладки.</summary>
        public const string SurfaceAltKey = "AvalonDock_SurfaceAlt";

        /// <summary>Основной текст.</summary>
        public const string ForegroundKey = "AvalonDock_Foreground";

        /// <summary>Неактивные вкладки и подписи.</summary>
        public const string ForegroundMutedKey = "AvalonDock_ForegroundMuted";

        /// <summary>Hairline-разделитель 1px.</summary>
        public const string SeparatorKey = "AvalonDock_Separator";

        /// <summary>Усиленный hairline: граница плавающего окна, hover ресайзера.</summary>
        public const string SeparatorStrongKey = "AvalonDock_SeparatorStrong";

        /// <summary>Выбранное состояние.</summary>
        public const string SelectionKey = "AvalonDock_Selection";

        /// <summary>Hover-состояние.</summary>
        public const string SelectionHoverKey = "AvalonDock_SelectionHover";

        /// <summary>Заливка рамки при перетаскивании (Surface @15%).</summary>
        public const string OverlayFillKey = "AvalonDock_OverlayFill";

        /// <summary>Обводка рамки при перетаскивании.</summary>
        public const string OverlayStrokeKey = "AvalonDock_OverlayStroke";

        /// <summary>Hover в переключателе документов (Ctrl+Tab).</summary>
        public const string NavigatorHoverKey = "AvalonDock_NavigatorHover";

        /// <summary>Выделение в переключателе документов.</summary>
        public const string NavigatorSelectedKey = "AvalonDock_NavigatorSelected";

        /// <summary>Выделение в неактивном переключателе документов.</summary>
        public const string NavigatorSelectedInactiveKey = "AvalonDock_NavigatorSelectedInactive";

        /// <summary>Hover по выделенной строке переключателя документов.</summary>
        public const string NavigatorSelectedHoverKey = "AvalonDock_NavigatorSelectedHover";

        /// <summary>Фон кнопки закрытия под курсором (единственный не-нейтральный цвет).</summary>
        public const string CloseHoverBackgroundKey = "AvalonDock_CloseHoverBackground";

        /// <summary>Текст кнопки закрытия под курсором.</summary>
        public const string CloseHoverForegroundKey = "AvalonDock_CloseHoverForeground";

        /// <summary>Все ключи, которые обязан заполнять <see cref="Apply(ResourceDictionary, ResourceDictionary)"/>.</summary>
        public static readonly IReadOnlyList<string> Keys = new[]
        {
            SurfaceKey,
            SurfaceAltKey,
            ForegroundKey,
            ForegroundMutedKey,
            SeparatorKey,
            SeparatorStrongKey,
            SelectionKey,
            SelectionHoverKey,
            OverlayFillKey,
            OverlayStrokeKey,
            NavigatorHoverKey,
            NavigatorSelectedKey,
            NavigatorSelectedInactiveKey,
            NavigatorSelectedHoverKey,
            CloseHoverBackgroundKey,
            CloseHoverForegroundKey
        };

        /// <summary>Нейтральные значения по умолчанию (тёмная палитра студии).</summary>
        private static readonly Dictionary<string, Color> Fallbacks = new Dictionary<string, Color>
        {
            [SurfaceKey] = Color.FromRgb(0x27, 0x27, 0x27),
            [SurfaceAltKey] = Color.FromRgb(0x2E, 0x2E, 0x2F),
            [ForegroundKey] = Color.FromRgb(0xF1, 0xF1, 0xF1),
            [ForegroundMutedKey] = Color.FromRgb(0x9D, 0xA0, 0xA6),
            [SeparatorKey] = Color.FromRgb(0x37, 0x37, 0x37),
            [SeparatorStrongKey] = Color.FromRgb(0x48, 0x48, 0x4F),
            [SelectionKey] = Color.FromRgb(0x40, 0x40, 0x46),
            [SelectionHoverKey] = Color.FromRgb(0x33, 0x36, 0x38),
            [OverlayFillKey] = Color.FromArgb(0x26, 0x27, 0x27, 0x27),
            [OverlayStrokeKey] = Color.FromRgb(0x48, 0x48, 0x4F),
            [NavigatorHoverKey] = Color.FromRgb(0x33, 0x36, 0x38),
            [NavigatorSelectedKey] = Color.FromRgb(0x40, 0x40, 0x46),
            [NavigatorSelectedInactiveKey] = Color.FromRgb(0x2E, 0x2E, 0x2F),
            [NavigatorSelectedHoverKey] = Color.FromRgb(0x33, 0x36, 0x38),
            [CloseHoverBackgroundKey] = Color.FromRgb(0xE8, 0x11, 0x23),
            [CloseHoverForegroundKey] = Color.FromRgb(0xFF, 0xFF, 0xFF)
        };

        /// <summary>Копирует кисти палитры из ресурсов приложения в <paramref name="target"/>.</summary>
        public static void Apply(ResourceDictionary target)
        {
            Apply(target, Application.Current?.Resources);
        }

        /// <summary>
        /// Копирует кисти палитры из <paramref name="source"/> в <paramref name="target"/>.
        /// Отсутствующие в источнике ключи заполняются нейтральными fallback-значениями.
        /// </summary>
        public static void Apply(ResourceDictionary target, ResourceDictionary source)
        {
            if (target == null)
            {
                return;
            }

            foreach (string key in Keys)
            {
                if (TryResolve(source, key, out Color color) || Fallbacks.TryGetValue(key, out color))
                {
                    Set(target, key, color);
                }
            }
        }

        private static bool TryResolve(ResourceDictionary source, string key, out Color color)
        {
            color = default;
            if (source == null || key == null || !source.Contains(key))
            {
                return false;
            }

            if (source[key] is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }

            return false;
        }

        private static void Set(ResourceDictionary target, string key, Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();

            // Remove + Add вместо простого присваивания: гарантирует уведомление
            // DynamicResource-ссылок при смене темы.
            target.Remove(key);
            target[key] = brush;
        }
    }
}
