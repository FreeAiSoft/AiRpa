using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

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
    /// <para>
    /// Шаблоны WF собирают светлые кисти в обход палитры — литералами стиля/шаблона (например фон заголовка
    /// категории тулбокса <c>#E9ECEE</c> и системный hover <c>#FFE8A6</c> темы Aero2), поэтому цвета,
    /// которые нельзя сменить ресурсом, гасятся проходом по эффективным значениям —
    /// <see cref="NeutralizeLightBrushes"/>. Стили WF при этом остаются нетронутыми: в них живут шаблоны
    /// элементов и обработчики перетаскивания активностей (<c>ToolboxDragContractTests</c>).
    /// </para>
    /// </summary>
    internal static class WfThemeStyles
    {
        /// <summary>Шрифт WF-контролов: Roboto для всего проекта (единственный источник — <see cref="StudioFonts"/>).</summary>
        public const string FontFamilyName = StudioFonts.FamilyName;

        /// <summary>Размер шрифта WF-контролов (дефолт WF — 12; менять только здесь).</summary>
        public const double FontSize = 12d;

        /// <summary>
        /// Минимальная разница яркости, при которой кисть считается «светлой» относительно поверхности палитры.
        /// </summary>
        internal const double LightPlateThreshold = 0.25;

        /// <summary>
        /// Минимальный «запас» яркости, ниже которого текст не читается на поверхности (тёмная тема).
        /// </summary>
        internal const double ReadableTextThreshold = 0.1;
        private static readonly object ThemedMarker = new object();

        /// <summary>Отметка палитры, которой окрашен стиль темизатора (нужна при смене темы).</summary>
        private static readonly object ThemedPaletteMarker = new object();

        private static readonly ConcurrentDictionary<Type, IReadOnlyList<DependencyProperty>> BrushPropertyCache =
            new ConcurrentDictionary<Type, IReadOnlyList<DependencyProperty>>();

        /// <summary>Подписки на состояния элементов (см. <see cref="WatchState"/>), по одной на элемент.</summary>
        private static readonly ConditionalWeakTable<DependencyObject, StateHook> StateHooks =
            new ConditionalWeakTable<DependencyObject, StateHook>();

        /// <summary>Шрифт для сеттеров стилей (единственный источник — <see cref="StudioFonts"/>).</summary>
        public static FontFamily Font { get; } = StudioFonts.UiFont;

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

        /// <summary>
        /// Помечает стиль как созданный темизатором и запоминает палитру: при смене темы обёртка
        /// пересоздаётся от исходного стиля WF, иначе элементы остались бы на кистях прежней темы.
        /// </summary>
        public static void MarkThemed(Style style, StudioThemeColors palette)
        {
            MarkThemed(style);

            if (style != null && palette != null)
            {
                style.Resources[ThemedPaletteMarker] = palette;
            }
        }

        /// <summary>Палитра, которой окрашен стиль темизатора (<c>null</c> — стиль не наш).</summary>
        public static StudioThemeColors PaletteOf(Style style)
        {
            for (Style current = style; current != null; current = current.BasedOn)
            {
                if (current.Resources.Contains(ThemedPaletteMarker))
                {
                    return current.Resources[ThemedPaletteMarker] as StudioThemeColors;
                }
            }

            return null;
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
        /// шаблон (например элемент Toolbox с иконкой активити) сохраняется через <c>BasedOn</c>.
        /// </summary>
        public static Style Wrap(Type targetType, Style baseStyle)
        {
            bool canWrap = baseStyle != null
                && baseStyle.TargetType != null
                && baseStyle.TargetType.IsAssignableFrom(targetType);

            return canWrap ? new Style(targetType, baseStyle) : new Style(targetType);
        }

        // --- Гашение светлых кистей шаблонов WF (плашки заголовков, chrome, стрелки раскрытия). ---

        /// <summary>
        /// Гасит светлые кисти внутри поддерева, не подменяя стили WF.
        /// <para>
        /// Регресс, ради которого проход появился в тулбоксе: заголовок категории в тёмной теме стоял на
        /// белой плашке. WF-стиль категории задаёт контейнеру <c>Background = #E9ECEE</c>, а шаблон элемента
        /// повторяет его через <c>TemplateBinding</c> (<c>Border#Bd</c>); значение зашито в стиль, поэтому
        /// подмена системных кистей (<see cref="StudioBrushes"/>) на него не влияет.
        /// </para>
        /// <para>
        /// Значения читаются эффективно (<see cref="DependencyObject.GetValue"/>), а не только локальные:
        /// цвет детей шаблона приходит из шаблона и локальным значением не является. Обходятся <b>все</b>
        /// свойства типа <see cref="Brush"/> (рефлексия по типу элемента, кэш на тип). Кисти палитры не
        /// трогаются — это и есть результат темизации.
        /// </para>
        /// </summary>
        /// <param name="root">Корень обхода.</param>
        /// <param name="palette">Активная палитра.</param>
        /// <param name="stopAtItemContainers">
        /// Не заходить внутрь контейнеров вложенных элементов (<see cref="TreeViewItem"/>,
        /// <see cref="ListBoxItem"/>): у каждого контейнера свой проход, иначе обход дерева тулбокса
        /// становится квадратичным.
        /// </param>
        internal static void NeutralizeLightBrushes(
            DependencyObject root,
            StudioThemeColors palette,
            bool stopAtItemContainers = false)
        {
            if (root == null || palette == null)
            {
                return;
            }

            double surfaceLuminance = Luminance(palette.Surface);
            HashSet<Color> paletteColors = new HashSet<Color>(palette.Tokens.Values);
            SolidColorBrush surface = Brush(palette.Surface);
            SolidColorBrush foreground = Brush(palette.Foreground);
            SolidColorBrush muted = Brush(palette.ForegroundMuted);
            bool darkTheme = Luminance(palette.Foreground) > surfaceLuminance;

            foreach (DependencyObject element in Walk(root, stopAtItemContainers))
            {
                foreach (DependencyProperty property in BrushPropertiesOf(element.GetType()))
                {
                    if (!(element.GetValue(property) is Brush brush))
                    {
                        continue;
                    }

                    // Привязку не разрываем: кисть из модели — не артефакт шаблона.
                    if (BindingOperations.IsDataBound(element, property))
                    {
                        continue;
                    }

                    string name = property.Name;

                    if (string.Equals(name, "Foreground", StringComparison.Ordinal))
                    {
                        if (TryMapStalePaletteColour(brush, palette, out Color staleForeground))
                        {
                            // Кисть предыдущей темы (тёмная на светлой теме и наоборот).
                            element.SetValue(property, Brush(staleForeground));
                            continue;
                        }

                        // Тёмный текст на тёмной поверхности не читается; в светлой палитре Foreground
                        // сам тёмный, поэтому правило включается только для тёмной темы.
                        if (darkTheme && IsDarkTextColour(brush, surfaceLuminance, paletteColors))
                        {
                            element.SetValue(property, foreground);
                        }

                        continue;
                    }

                    // Стрелка раскрытия живёт внутри ToggleButton-экспандера и в шаблонах WF/Aero2
                    // закрашена системными цветами: на тёмной поверхности её просто не видно.
                    if (element is Shape glyph && IsExpanderGlyph(glyph))
                    {
                        glyph.Fill = muted;
                        glyph.Stroke = muted;
                        continue;
                    }

                    bool areaOrLine = string.Equals(name, "Background", StringComparison.Ordinal) || IsBorderLike(property);
                    if (areaOrLine && TryMapStalePaletteColour(brush, palette, out Color stale))
                    {
                        // Площадь/линия, которую темизатор покрасил в палитру предыдущей темы.
                        element.SetValue(property, Brush(stale));
                        continue;
                    }

                    if (IsLightBrush(brush, surfaceLuminance, paletteColors))
                    {
                        // Светлая площадь гасится в цвет поверхности, а не в Transparent: прозрачный фон
                        // показывает светлого предка (шаблоны WF приходят со {StaticResource}) и плашка
                        // остаётся. Линии (рамки, обводки) прозрачны — под ними уже есть подложка.
                        element.SetValue(property, IsBorderLike(property) ? Brushes.Transparent : surface);
                    }
                }
            }
        }

        /// <summary>
        /// Свойства типа <see cref="Brush"/> элемента, включая унаследованные: рефлексия выполняется один
        /// раз на тип (<see cref="BrushPropertyCache"/>), а не на каждое применение темы.
        /// </summary>
        internal static IReadOnlyList<DependencyProperty> BrushPropertiesOf(Type type)
        {
            return type == null
                ? Array.Empty<DependencyProperty>()
                : BrushPropertyCache.GetOrAdd(type, key =>
            {
                List<DependencyProperty> properties = new List<DependencyProperty>();

                for (Type current = key; current != null && current != typeof(object); current = current.BaseType)
                {
                    foreach (FieldInfo field in current.GetFields(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (field.FieldType != typeof(DependencyProperty)
                            || !(field.GetValue(null) is DependencyProperty property)
                            || !typeof(Brush).IsAssignableFrom(property.PropertyType))
                        {
                            continue;
                        }

                        if (!properties.Contains(property))
                        {
                            properties.Add(property);
                        }
                    }
                }

                return properties;
            });
        }

        /// <summary>
        /// Светлая ли кисть относительно поверхности палитры (фон, рамка, заливка, обводка). Кисти палитры
        /// светлыми не считаются, иначе темизатор гасил бы собственную работу — текст, шевроны, состояния.
        /// </summary>
        private static bool IsLightBrush(Brush brush, double surfaceLuminance, HashSet<Color> paletteColors)
        {
            if (brush is SolidColorBrush solid)
            {
                return IsLightColour(solid.Color, surfaceLuminance, paletteColors);
            }

            if (brush is GradientBrush gradient)
            {
                foreach (GradientStop stop in gradient.GradientStops)
                {
                    if (IsLightColour(stop.Color, surfaceLuminance, paletteColors))
                    {
                        return true;
                    }
                }
            }

            // ImageBrush/DrawingBrush/VisualBrush не оцениваются: внутри контрола это содержимое, а не фон.
            return false;
        }

        private static bool IsLightColour(Color color, double surfaceLuminance, HashSet<Color> paletteColors)
        {
            return color.A != 0
                && !paletteColors.Contains(color)
                && Luminance(color) > surfaceLuminance + LightPlateThreshold;
        }

        /// <summary>Тёмный (нечитаемый на тёмной поверхности) цвет текста.</summary>
        private static bool IsDarkTextColour(Brush brush, double surfaceLuminance, HashSet<Color> paletteColors)
        {
            return brush is SolidColorBrush solid
                && solid.Color.A != 0
                && !paletteColors.Contains(solid.Color)
                && Luminance(solid.Color) <= surfaceLuminance + ReadableTextThreshold;
        }

        /// <summary>
        /// Свойство рисует линию (рамка, обводка, контур), а не площадь: такие кисти можно погасить в
        /// <c>Transparent</c> — под ними уже нарисована непрозрачная подложка.
        /// </summary>
        private static bool IsBorderLike(DependencyProperty property)
        {
            string name = property?.Name ?? string.Empty;

            return name.IndexOf("Border", StringComparison.Ordinal) >= 0
                || name.StartsWith("Stroke", StringComparison.Ordinal)
                || name.IndexOf("Outline", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Кисть осталась от предыдущей темы: смена палитры не перекрашивает локальные значения, поэтому
        /// тёмная плашка пережила бы переключение на светлую тему. Токен узнаётся по цвету противоположной
        /// палитры и заменяется тем же токеном активной.
        /// </summary>
        internal static bool TryMapStalePaletteColour(Brush brush, StudioThemeColors palette, out Color mapped)
        {
            mapped = default;

            if (!(brush is SolidColorBrush solid) || solid.Color.A != 0xFF)
            {
                return false;
            }

            Color colour = solid.Color;
            if (palette.Tokens.Values.Contains(colour))
            {
                return false;
            }

            StudioThemeColors other = palette.IsDark ? StudioPalette.Light : StudioPalette.Dark;
            foreach (KeyValuePair<string, Color> token in other.Tokens)
            {
                if (token.Value == colour && palette.Tokens.TryGetValue(token.Key, out mapped) && mapped != colour)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Стрелка раскрытия: <see cref="Shape"/> внутри <see cref="ToggleButton"/>-экспандера шаблона.</summary>
        private static bool IsExpanderGlyph(Shape shape)
        {
            return XamlHelper.FindVisualParent<ToggleButton>(shape) != null;
        }

        /// <summary>Относительная яркость цвета (WCAG): 0 — чёрный, 1 — белый.</summary>
        private static double Luminance(Color color)
        {
            return (0.2126 * LinearChannel(color.R)) + (0.7152 * LinearChannel(color.G)) + (0.0722 * LinearChannel(color.B));
        }

        private static double LinearChannel(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// Подписка на состояния элемента (hover, выделение, фокус): палитра пишется <b>локальными</b>
        /// значениями и обновляется при изменении состояния.
        /// <para>
        /// Зачем не хватает триггеров стиля: шаблоны системной темы (Aero2) красят фон и рамку своими
        /// триггерами, а триггер шаблона сильнее сеттера и триггера стиля — светлая плашка шапки таблицы,
        /// системное выделение строки и «жёлтая» рамка элемента тулбокса возвращались именно так.
        /// Локальное значение (<see cref="DependencyObject.SetValue(DependencyProperty, object)"/>) сильнее
        /// триггеров шаблона, поэтому цвет ставится из обработчика состояний.
        /// </para>
        /// <para>
        /// Подписка ставится один раз на элемент и снимается вместе с ним: генераторы контейнеров
        /// (строки таблицы, элементы списков) пересоздают контейнеры, и обработчики не должны копиться.
        /// Повторный вызов с новой палитрой сразу перекрашивает элемент — смена темы не ждёт смены состояния.
        /// </para>
        /// </summary>
        /// <param name="element">Элемент, состояние которого влияет на цвета.</param>
        /// <param name="palette">Активная палитра.</param>
        /// <param name="apply">Что применить: (элемент, палитра) → локальные кисти.</param>
        /// <param name="properties">Свойства-состояния, изменения которых перерисовывают элемент.</param>
        internal static void WatchState(
            DependencyObject element,
            StudioThemeColors palette,
            Action<DependencyObject, StudioThemeColors> apply,
            params DependencyProperty[] properties)
        {
            if (element == null || palette == null || apply == null)
            {
                return;
            }

            StateHook hook = StateHooks.GetValue(element, key => new StateHook());
            hook.Palette = palette;
            hook.Apply = apply;

            if (hook.Subscribed)
            {
                hook.Apply(element, palette);
                return;
            }

            hook.Subscribed = true;
            hook.Handler = (sender, args) => hook.Apply(element, hook.Palette);

            foreach (DependencyProperty property in properties)
            {
                DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
                if (descriptor == null)
                {
                    continue;
                }

                descriptor.AddValueChanged(element, hook.Handler);
                hook.Descriptors.Add(descriptor);
            }

            if (element is FrameworkElement frameworkElement)
            {
                frameworkElement.Unloaded += (sender, args) => UnwatchState((DependencyObject)sender);
            }

            hook.Apply(element, palette);
        }

        /// <summary>Снимает подписки на состояния элемента (повторная темизация подпишет заново).</summary>
        internal static void UnwatchState(DependencyObject element)
        {
            if (element == null || !StateHooks.TryGetValue(element, out StateHook hook) || !hook.Subscribed)
            {
                return;
            }

            hook.Subscribed = false;

            foreach (DependencyPropertyDescriptor descriptor in hook.Descriptors)
            {
                descriptor.RemoveValueChanged(element, hook.Handler);
            }

            hook.Descriptors.Clear();
        }

        /// <summary>Сколько раз элемент подписывался через <see cref="WatchState"/> (стражи идемпотентности).</summary>
        internal static int WatchStateCount(DependencyObject element)
        {
            return element != null && StateHooks.TryGetValue(element, out StateHook hook) && hook.Subscribed ? 1 : 0;
        }

        /// <summary>Подписка на состояния элемента и палитра, которой он рисуется.</summary>
        private sealed class StateHook
        {
            public StudioThemeColors Palette { get; set; }

            public Action<DependencyObject, StudioThemeColors> Apply { get; set; }

            public EventHandler Handler { get; set; }

            public List<DependencyPropertyDescriptor> Descriptors { get; } = new List<DependencyPropertyDescriptor>();

            public bool Subscribed { get; set; }
        }

        /// <summary>Корень и его потомки в визуальном дереве (не-визуальные узлы пропускаются).</summary>
        private static IEnumerable<DependencyObject> Walk(DependencyObject root, bool stopAtItemContainers)
        {
            if (root == null)
            {
                yield break;
            }

            yield return root;

            if (!XamlHelper.IsVisual(root))
            {
                yield break;
            }

            foreach (DependencyObject child in XamlHelper.Children(root))
            {
                if (stopAtItemContainers && (child is TreeViewItem || child is ListBoxItem))
                {
                    continue;
                }

                foreach (DependencyObject descendant in Walk(child, stopAtItemContainers))
                {
                    yield return descendant;
                }
            }
        }
    }
}
