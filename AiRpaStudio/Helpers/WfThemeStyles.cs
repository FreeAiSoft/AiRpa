using System;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Примитивы темизации нативных WF-контролов (<c>ToolboxControl</c>, <c>OutlineView</c>,
    /// <c>PropertyInspectorView</c>) — этап 6 плана docs\Vs2026_Theme_Plan.md, F-01…F-03.
    /// <para>
    /// Значения берутся только из <see cref="StudioPalette"/>: акцент MaterialDesign
    /// (<c>MaterialThemePalette</c>) в WF-контролах больше не используется, поэтому выделение,
    /// hover и фокус передаются яркостью палитры (Hover/Selected/Separator/…), а не цветом акцента.
    /// </para>
    /// <para>
    /// Кисти создаются замороженными (<see cref="Freezable.Freeze"/>) — темизация вызывается на каждом
    /// открытии документа и каждой смене темы, заморозка снимает нагрузку на поток отрисовки.
    /// </para>
    /// <para>
    /// Стили, созданные темизатором, помечаются маркером в <see cref="Style.Resources"/>: повторный вызов
    /// не должен наращивать цепочку <see cref="Style.BasedOn"/> (иначе каждый Ctrl+Tab плодит стили).
    /// </para>
    /// </summary>
    internal static class WfThemeStyles
    {
        /// <summary>Шрифт WF-контролов: Consolas из шаблонов WF заменена на Segoe UI.</summary>
        public const string FontFamilyName = "Segoe UI";

        private static readonly FontFamily UiFont = new FontFamily(FontFamilyName);
        private static readonly object ThemedMarker = new object();

        /// <summary>Замороженный шрифт для сеттеров стилей.</summary>
        public static FontFamily Font => UiFont;

        /// <summary>Замороженная кисть палитры.</summary>
        public static SolidColorBrush Brush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>Помечает стиль как созданный темизатором.</summary>
        public static void MarkThemed(Style style)
        {
            if (style != null && !style.Resources.Contains(ThemedMarker))
            {
                style.Resources[ThemedMarker] = ThemedMarker;
            }
        }

        /// <summary>Стиль (или любой его предок в цепочке <see cref="Style.BasedOn"/>) создан темизатором.</summary>
        public static bool IsThemed(Style style)
        {
            for (Style current = style; current != null; current = current.BasedOn)
            {
                if (current.Resources.Contains(ThemedMarker))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Оборачивает существующий стиль контрола в новый с тем же <see cref="Style.TargetType"/>:
        /// шаблон (например, элемент Toolbox с иконкой активити) сохраняется через <c>BasedOn</c>.
        /// </summary>
        public static Style Wrap(Type targetType, Style baseStyle)
        {
            bool canWrap = baseStyle != null
                && baseStyle.TargetType != null
                && baseStyle.TargetType.IsAssignableFrom(targetType);

            return canWrap ? new Style(targetType, baseStyle) : new Style(targetType);
        }
    }
}
