using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Activities.Presentation;
using System.Activities.Presentation.View;

namespace RPA.Helpers
{
    /// <summary>
    /// Тема <b>активностей</b> дизайнера WF: плита активности, её рамка, подпись и контролы внутри.
    /// <para>
    /// Почему отдельный проход. Активность рисует шаблон WF
    /// (<c>WorkflowElementDesignerDefaultTemplate.xaml</c>), а цвета он берёт из
    /// <c>System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml</c>,
    /// который WF подмешивает в <c>ControlTemplate.Resources</c> <b>своего</b> шаблона. Этот словарь ближе
    /// к элементу, чем словари приложения/дизайнера, поэтому ключи <c>WorkflowViewElementBackground</c> и
    /// <c>DesignerViewBackground</c> из <see cref="WfDesignerTheme"/> до активностей не доходят — плита
    /// остаётся белой, плашка заголовка светло-серой (<c>#FFE9ECEE</c>), рамка голубой
    /// (<c>#FFD0D7E2</c>). Проверено пробником <c>tools\designer-ui-diagnostics</c>.
    /// </para>
    /// <para>
    /// Подменить значения в WF-словаре нельзя: кисти в нём <b>заморожены</b>, а сам словарь после загрузки
    /// шаблонов становится <b>read-only</b> (<c>InvalidOperationException</c> на записи). Поэтому тёмная тема
    /// ставится <b>локальными значениями</b> на сами элементы шаблона: локальное значение сильнее
    /// <c>DynamicResource</c>/<c>StaticResource</c> шаблона и переживает смену темы. Активности, созданные
    /// позже (перетаскивание из панели «Активности», «провал» в активность), достраиваются по
    /// <see cref="FrameworkElement.LayoutUpdated"/> — проход идемпотентен и после первой покраски только
    /// читает значения.
    /// </para>
    /// <para>
    /// Вторая часть — изоляция от MaterialDesign. <c>MaterialDesign2.Defaults.xaml</c> из <c>App.xaml</c>
    /// даёт неявные стили для <see cref="Button"/>/<see cref="CheckBox"/>/<see cref="TextBox"/> и т.п., и они
    /// перебивают нативную форму WF внутри активностей (проверено: <c>CheckBox</c> из <c>ActivityTop</c>
    /// получал стиль MaterialDesign 29×29, <c>Button#AnnotationIndicator</c> — шрифт 14, кнопки
    /// «Развернуть все»/«Свернуть все» — <c>Width = 34</c>). Поэтому в ресурсы дизайнера и каждой
    /// активности кладутся пустые неявные стили-заглушки: в более близком словаре они перекрывают
    /// MaterialDesign, контрол возвращается к своему нативному шаблону (форма и шрифт WF/Aero2), а цвета
    /// ему ставит этот же проход из <see cref="StudioPalette"/>.
    /// </para>
    /// </summary>
    internal static class WfActivityTheme
    {
        /// <summary>Маркер словаря с «нативными» стилями-заглушками (повторный вызов ничего не добавляет).</summary>
        internal const string NativeStylesMarker = "Studio.WfActivityTheme.NativeStyles";

        /// <summary>Порог «светлой» кисти относительно поверхности палитры (как в <see cref="WfThemeStyles"/>).</summary>
        private const double LightThreshold = WfThemeStyles.LightPlateThreshold;

        /// <summary>Порог «тёмного текста» (как в <see cref="WfThemeStyles"/>).</summary>
        private const double TextThreshold = WfThemeStyles.ReadableTextThreshold;

        /// <summary>Максимальная высота плашки, которая считается полосой заголовка активности, px.</summary>
        private const double TitleBarHeight = 30d;

        /// <summary>
        /// Контролы, у которых неявный стиль MaterialDesign перебивает нативную форму WF.
        /// <para>
        /// <see cref="ScrollBar"/> здесь нет намеренно: плоские скроллы 10 px задаёт
        /// <c>Themes\StudioScrollBar.xaml</c>, и заглушка вернула бы системную полосу прокрутки.
        /// </para>
        /// </summary>
        private static readonly Type[] NativeStyleTypes =
        {
            typeof(Button),
            typeof(RepeatButton),
            typeof(ToggleButton),
            typeof(CheckBox),
            typeof(RadioButton),
            typeof(TextBox),
            typeof(PasswordBox),
            typeof(ComboBox),
            typeof(ComboBoxItem)
        };

        /// <summary>Подписки на раскладку корня дизайнера: достраивают активности, созданные после темизации.</summary>
        private static readonly ConditionalWeakTable<FrameworkElement, LayoutHook> LayoutHooks =
            new ConditionalWeakTable<FrameworkElement, LayoutHook>();

        /// <summary>Активности, которым уже поставлена палитра (проход по раскладке их пропускает).</summary>
        private static readonly ConditionalWeakTable<WorkflowViewElement, ActivityMark> Marks =
            new ConditionalWeakTable<WorkflowViewElement, ActivityMark>();

        /// <summary>
        /// Применяет палитру к активностям дизайнера. Безопасно вызывать многократно: повторный проход
        /// только читает уже покрашенные значения.
        /// </summary>
        internal static void Apply(FrameworkElement root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            UseNativeControlStyles(root.Resources);
            ThemeActivities(root, palette);
            HookLayout(root, palette);
        }

        /// <summary>Темизирует все активности поддерева (включая сам узел, если это активность).</summary>
        internal static void ThemeActivities(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            if (root is WorkflowViewElement self)
            {
                ThemeActivity(self, palette);
            }

            foreach (WorkflowViewElement activity in XamlHelper.FindVisualChildren<WorkflowViewElement>(root))
            {
                ThemeActivity(activity, palette);
            }
        }

        /// <summary>
        /// Темизирует одну активность: плита, рамка и подпись ставятся локальными значениями (значения
        /// шаблона WF запечены через <c>StaticResource</c>/<c>DynamicResource</c> WF-словаря и при смене
        /// темы не пересчитываются), затем гасятся светлые плашки и подтягиваются линии внутри содержимого.
        /// </summary>
        internal static void ThemeActivity(WorkflowViewElement activity, StudioThemeColors palette)
        {
            if (activity == null || palette == null)
            {
                return;
            }

            SetBrush(activity, Control.BackgroundProperty, WfThemeStyles.Brush(palette.Surface));
            SetBrush(activity, Control.BorderBrushProperty, WfThemeStyles.Brush(palette.SeparatorStrong));
            SetBrush(activity, Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground));

            UseNativeControlStyles(activity.Resources);
            RepaintActivity(activity, palette);

            Marks.Remove(activity);
            Marks.Add(activity, new ActivityMark
            {
                Palette = palette,
                Children = VisualTreeHelper.GetChildrenCount(activity)
            });
        }

        /// <summary>
        /// Кладёт в словарь пустые неявные стили для контролов, которые иначе получают стиль MaterialDesign
        /// из <c>App.xaml</c>. Словарь помечается маркером: повторный вызов ничего не добавляет, а стили
        /// пользователя/темы (например <see cref="ScrollBar"/>) не подменяются.
        /// </summary>
        internal static void UseNativeControlStyles(ResourceDictionary target)
        {
            if (target == null || target.Contains(NativeStylesMarker))
            {
                return;
            }

            foreach (Type type in NativeStyleTypes)
            {
                if (!target.Contains(type))
                {
                    target[type] = CreateNativeStyle(type);
                }
            }

            target[NativeStylesMarker] = NativeStylesMarker;
        }

        /// <summary>
        /// Неявный стиль без сеттеров: в более близком словаре он перекрывает стиль MaterialDesign, и
        /// контрол возвращается к своему нативному шаблону (форма и шрифт WF/Aero2). Цвета ему ставит
        /// темизатор — точечно, а не шаблоном.
        /// </summary>
        internal static Style CreateNativeStyle(Type targetType)
        {
            return targetType == null ? null : new Style(targetType);
        }

        /// <summary>
        /// Гасит светлые плашки и поднимает линии внутри активности: то, что пришло из шаблона WF запечённым
        /// через <c>StaticResource</c> и потому не пересчитывается при смене темы. Кисти палитры не
        /// трогаются — это и есть результат темизации; привязки модели не разрываются.
        /// </summary>
        internal static void RepaintActivity(DependencyObject activity, StudioThemeColors palette)
        {
            if (activity == null || palette == null)
            {
                return;
            }

            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush surfaceAlt = WfThemeStyles.Brush(palette.SurfaceAlt);
            SolidColorBrush separatorStrong = WfThemeStyles.Brush(palette.SeparatorStrong);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);
            SolidColorBrush foregroundMuted = WfThemeStyles.Brush(palette.ForegroundMuted);

            double surfaceLuminance = Luminance(palette.Surface);
            HashSet<Color> paletteColours = ThemedColours(palette);
            bool darkTheme = Luminance(palette.Foreground) > surfaceLuminance;

            foreach (DependencyObject element in SelfAndDescendants(activity))
            {
                RestoreStale(element, palette);

                if (element is Rectangle outline && IsActivityOutline(outline))
                {
                    // Рамка активности: светлая линия палитры вместо белой обводки шаблона WF,
                    // заливка — цвет поверхности (иначе под обводкой остаётся светлая плита).
                    SetBrush(outline, Shape.FillProperty, surface);
                    SetBrush(outline, Shape.StrokeProperty, separatorStrong);
                    continue;
                }

                if (element is Shape shape)
                {
                    // Глифы (стрелка раскрытия, коннекторы): системный светлый/тёмный цвет на плите
                    // палитры не читается.
                    if (IsNeutral(shape.Fill) || IsLight(shape.Fill, surfaceLuminance, paletteColours))
                    {
                        SetBrush(shape, Shape.FillProperty, foregroundMuted);
                    }

                    if (IsNeutral(shape.Stroke) && IsLight(shape.Stroke, surfaceLuminance, paletteColours))
                    {
                        SetBrush(shape, Shape.StrokeProperty, foregroundMuted);
                    }

                    continue;
                }

                if (element is Border border)
                {
                    if (IsLight(border.Background, surfaceLuminance, paletteColours))
                    {
                        // Полоса заголовка активности светлее плиты на один тон палитры.
                        SetBrush(
                            border,
                            Border.BackgroundProperty,
                            border.ActualHeight <= TitleBarHeight ? surfaceAlt : surface);
                    }

                    if (IsLight(border.BorderBrush, surfaceLuminance, paletteColours))
                    {
                        SetBrush(border, Border.BorderBrushProperty, separatorStrong);
                    }

                    continue;
                }

                if (element is Panel panel)
                {
                    if (IsLight(panel.Background, surfaceLuminance, paletteColours))
                    {
                        SetBrush(panel, Panel.BackgroundProperty, surface);
                    }

                    continue;
                }

                if (element is TextBlock textBlock)
                {
                    if (darkTheme && IsDarkText(textBlock.Foreground, surfaceLuminance, paletteColours))
                    {
                        SetBrush(textBlock, TextBlock.ForegroundProperty, foreground);
                    }

                    continue;
                }

                if (element is Control control)
                {
                    if (IsLight(control.Background, surfaceLuminance, paletteColours))
                    {
                        SetBrush(control, Control.BackgroundProperty, surface);
                    }

                    if (IsLight(control.BorderBrush, surfaceLuminance, paletteColours))
                    {
                        SetBrush(control, Control.BorderBrushProperty, separatorStrong);
                    }

                    if (darkTheme && IsDarkText(control.Foreground, surfaceLuminance, paletteColours))
                    {
                        SetBrush(control, Control.ForegroundProperty, foreground);
                    }
                }
            }
        }

        /// <summary>
        /// Возвращает кисти предыдущей темы: локальные значения, поставленные прошлым проходом
        /// (тёмная плашка на светлой теме и наоборот), пересчитываются тем же токеном активной палитры.
        /// <see cref="WfThemeStyles.TryMapStalePaletteColour"/> иначе элемент с тёмным фоном остался бы
        /// тёмным на светлой теме: правила «светлое гасим» его уже не видят.
        /// </summary>
        private static void RestoreStale(DependencyObject element, StudioThemeColors palette)
        {
            foreach (DependencyProperty property in WfThemeStyles.BrushPropertiesOf(element.GetType()))
            {
                if (BindingOperations.IsDataBound(element, property)
                    || !(element.GetValue(property) is Brush brush)
                    || !WfThemeStyles.TryMapStalePaletteColour(brush, palette, out Color mapped))
                {
                    continue;
                }

                element.SetValue(property, WfThemeStyles.Brush(mapped));
            }
        }

        /// <summary>Скруглённый прямоугольник — это рамка активности в шаблоне WF (<c>RadiusX/RadiusY &gt; 0</c>).</summary>
        private static bool IsActivityOutline(Rectangle rectangle)
        {
            return rectangle.RadiusX > 0d || rectangle.RadiusY > 0d;
        }

        /// <summary>Ставит кисть, не разрывая привязку модели.</summary>
        private static void SetBrush(DependencyObject element, DependencyProperty property, Brush brush)
        {
            if (element == null || property == null || brush == null)
            {
                return;
            }

            if (BindingOperations.IsDataBound(element, property))
            {
                return;
            }

            element.SetValue(property, brush);
        }

        private static bool IsLight(Brush brush, double surfaceLuminance, HashSet<Color> paletteColours)
        {
            if (!(brush is SolidColorBrush solid) || solid.Color.A == 0 || paletteColours.Contains(solid.Color))
            {
                return false;
            }

            return Luminance(solid.Color) > surfaceLuminance + LightThreshold;
        }

        private static bool IsDarkText(Brush brush, double surfaceLuminance, HashSet<Color> paletteColours)
        {
            if (!(brush is SolidColorBrush solid) || solid.Color.A == 0 || paletteColours.Contains(solid.Color))
            {
                return false;
            }

            return Luminance(solid.Color) <= surfaceLuminance + TextThreshold;
        }

        /// <summary>Серый системный цвет (белый/чёрный/светло-серый): такой глиф красится палитрой.</summary>
        private static bool IsNeutral(Brush brush)
        {
            if (!(brush is SolidColorBrush solid) || solid.Color.A == 0)
            {
                return false;
            }

            Color colour = solid.Color;
            int max = Math.Max(colour.R, Math.Max(colour.G, colour.B));
            int min = Math.Min(colour.R, Math.Min(colour.G, colour.B));

            return max - min <= 16;
        }

        /// <summary>
        /// Цвета, которые уже являются результатом темизации: нейтральные и текстовые токены палитры.
        /// Семантика (<c>CloseHover*</c>) в набор не входит: её белый и красный совпадают с системными
        /// цветами шаблонов WF, и проверка «это цвет палитры» оставила бы плиту активности белой.
        /// </summary>
        private static HashSet<Color> ThemedColours(StudioThemeColors palette)
        {
            HashSet<Color> colours = new HashSet<Color>();

            foreach (string name in StudioPalette.NeutralTokenNames.Concat(StudioPalette.TextTokenNames))
            {
                if (palette.Tokens.TryGetValue(name, out Color colour))
                {
                    colours.Add(colour);
                }
            }

            return colours;
        }


        private static double Luminance(Color colour)
        {
            return (0.299d * colour.R + 0.587d * colour.G + 0.114d * colour.B) / 255d;
        }

        private static IEnumerable<DependencyObject> SelfAndDescendants(DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            yield return root;

            foreach (DependencyObject child in XamlHelper.Children(root))
            {
                foreach (DependencyObject descendant in SelfAndDescendants(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Достраивает темизацию активностей, созданных после прохода темы (перетаскивание из панели
        /// «Активности», «провал» в активность, отмена/повтор): один отложенный проход на цикл раскладки.
        /// Проход идемпотентен — уже покрашенные элементы только читаются.
        /// </summary>
        private static void HookLayout(FrameworkElement root, StudioThemeColors palette)
        {
            LayoutHook hook = LayoutHooks.GetValue(root, key => new LayoutHook());
            hook.Palette = palette;

            if (hook.Attached)
            {
                return;
            }

            hook.Attached = true;

            root.LayoutUpdated += (sender, args) =>
            {
                if (hook.Pending)
                {
                    return;
                }

                hook.Pending = true;

                root.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        hook.Pending = false;
                        ThemeStaleActivities(root, hook.Palette);
                    }));
            };
        }

        /// <summary>Темизирует только те активности, которым эта палитра ещё не поставлена (проход по раскладке).</summary>
        private static void ThemeStaleActivities(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            if (root is WorkflowViewElement self && IsStale(self, palette))
            {
                ThemeActivity(self, palette);
            }

            foreach (WorkflowViewElement activity in XamlHelper.FindVisualChildren<WorkflowViewElement>(root))
            {
                if (IsStale(activity, palette))
                {
                    ThemeActivity(activity, palette);
                }
            }
        }

        /// <summary>
        /// Активность нужно покрасить заново: другая палитра, ещё не темизирована или шаблон
        /// пересоздан (изменилось число детей — WF пересобрал визуал активности).
        /// </summary>
        private static bool IsStale(WorkflowViewElement activity, StudioThemeColors palette)
        {
            return !Marks.TryGetValue(activity, out ActivityMark mark)
                || !ReferenceEquals(mark.Palette, palette)
                || mark.Children != VisualTreeHelper.GetChildrenCount(activity);
        }

        /// <summary>Отметка темизации активности.</summary>
        private sealed class ActivityMark
        {
            public StudioThemeColors Palette { get; set; }

            public int Children { get; set; }
        }

        /// <summary>Подписка на раскладку корня дизайнера.</summary>
        private sealed class LayoutHook
        {
            public StudioThemeColors Palette { get; set; }

            public bool Attached { get; set; }

            public bool Pending { get; set; }
        }
    }
}
