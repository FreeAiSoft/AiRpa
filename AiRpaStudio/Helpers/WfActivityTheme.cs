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

        /// <summary>Маркер неявного стиля подписи с цветом палитры (только режим кэша).</summary>
        internal const string PaletteTextStyleMarker = "Studio.WfActivityTheme.PaletteText";

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

        private static readonly ConditionalWeakTable<FrameworkElement, TextHook> TextHooks =
            new ConditionalWeakTable<FrameworkElement, TextHook>();

        /// <summary>Подписки на раскладку корня дизайнера: достраивают активности, созданные после темизации.</summary>
        private static readonly ConditionalWeakTable<FrameworkElement, LayoutHook> LayoutHooks =
            new ConditionalWeakTable<FrameworkElement, LayoutHook>();

        /// <summary>Активности, которым уже поставлена палитра (проход по раскладке их пропускает).</summary>
        private static ConditionalWeakTable<WorkflowViewElement, ActivityMark> Marks =
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

            UseNativeControlStyles(root.Resources, palette);

            // Режим кэша (WfColorResourcesOverride): плита, рамка и цвета внутри шаблона приходят из
            // связанных кистей палитры на этапе построения элемента, поэтому пост-покраска плиты не нужна.
            // Остаётся текст: подпись активности наследует Foreground от WorkflowViewElement, а тот приходит
            // из темы WF (чёрный) и ресурсным ключом не переопределяется. Цвет ставится синхронно в
            // LayoutUpdated — до отрисовки кадра, поэтому кадра с чёрной подписью не бывает.
            if (WfColorResourcesOverride.IsInstalled)
            {
                ThemeStaleText(root, palette);
                HookTextLayout(root, palette);
                return;
            }

            ThemeActivities(root, palette);
            HookLayout(root, palette);
        }


        /// <summary>Сбрасывает отметки темизации (проверка/пробник: отметки живут на процесс).</summary>
        internal static void ForgetThemeMarks()
        {
            Marks = new ConditionalWeakTable<WorkflowViewElement, ActivityMark>();
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

            UseNativeControlStyles(activity.Resources, palette);
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
        /// из <c>App.xaml</c>, и (в режиме кэша) неявный стиль подписи с цветом палитры.
        /// <para>
        /// Подпись активности наследует <see cref="TextElement.Foreground"/> от <c>WorkflowViewElement</c>,
        /// а тот получает чёрный цвет из <b>темы WF</b> (не из <c>ColorResources.xaml</c>) — ресурсным ключом
        /// его не переопределить. Неявный стиль <see cref="TextBlock"/> в словаре документа сильнее
        /// унаследованного значения, поэтому подпись появляется сразу в палитре: без этого новая активность
        /// (перетаскивание из панели) первую раскладку рисовалась бы чёрным текстом на тёмной плите, а
        /// докрашивал бы её только отложенный проход по раскладке — то самое мерцание.
        /// </para>
        /// </summary>
        internal static void UseNativeControlStyles(ResourceDictionary target, StudioThemeColors palette)
        {
            if (target == null)
            {
                return;
            }

            if (!target.Contains(NativeStylesMarker))
            {
                foreach (Type type in NativeStyleTypes)
                {
                    if (!target.Contains(type))
                    {
                        target[type] = CreateNativeStyle(type);
                    }
                }

                target[NativeStylesMarker] = NativeStylesMarker;
            }

            ApplyPaletteTextStyle(target, palette);
        }

        /// <summary>
        /// Неявный стиль подписи дизайнера: <see cref="TextBlock.Foreground"/> из палитры, кисть связанная
        /// (смена темы перекрашивает подписи без прохода по дереву). Только режим кэша: в прежнем режиме
        /// подписи красит пост-покраска, и стиль остался бы со старым цветом после смены темы.
        /// </summary>
        private static void ApplyPaletteTextStyle(ResourceDictionary target, StudioThemeColors palette)
        {
            if (palette == null || !WfColorResourcesOverride.IsInstalled || target.Contains(PaletteTextStyleMarker))
            {
                return;
            }

            SolidColorBrush foreground = WfColorResourcesOverride.CreatePaletteBrush(
                StudioPalette.ForegroundToken,
                palette);

            if (foreground == null || target.Contains(typeof(TextBlock)))
            {
                return;
            }

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, foreground));
            target[typeof(TextBlock)] = style;
            target[PaletteTextStyleMarker] = PaletteTextStyleMarker;
        }


        /// <summary>Вариант без палитры: только стили-заглушки (прежний режим, вызовы без контекста темы).</summary>
        internal static void UseNativeControlStyles(ResourceDictionary target)
        {
            UseNativeControlStyles(target, null);
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

        /// <summary>
        /// Ставит цвет текста активностям, у которых он ещё не из палитры (режим кэша). Кисть связанная,
        /// поэтому смена темы перекрашивает подписи без повторного прохода по дереву.
        /// </summary>
        internal static void ThemeStaleText(DependencyObject root, StudioThemeColors palette)
        {
            SolidColorBrush foreground = WfColorResourcesOverride.CreatePaletteBrush(
                StudioPalette.ForegroundToken,
                palette);

            if (root == null || palette == null || foreground == null)
            {
                return;
            }

            if (root is WorkflowViewElement self && IsStale(self, palette))
            {
                ApplyTextBrush(self, foreground, palette);
            }

            foreach (WorkflowViewElement activity in XamlHelper.FindVisualChildren<WorkflowViewElement>(root))
            {
                if (IsStale(activity, palette))
                {
                    ApplyTextBrush(activity, foreground, palette);
                }
            }
        }

        private static void ApplyTextBrush(
            WorkflowViewElement activity,
            SolidColorBrush foreground,
            StudioThemeColors palette)
        {
            // Привязку не разрываем: цвет подписи/текста может приходить из модели.
            if (activity == null
                || foreground == null
                || palette == null
                || BindingOperations.IsDataBound(activity, Control.ForegroundProperty))
            {
                return;
            }

            if (activity.Foreground is SolidColorBrush current && current.Color == palette.Foreground)
            {
                return;
            }

            // Отметка обязательна: проход идёт из LayoutUpdated, и повторная запись цвета на каждом цикле
            // раскладки зациклила бы диспетчер (проверено пробником: раскладка не выходила в idle).
            Marks.Remove(activity);
            Marks.Add(activity, new ActivityMark
            {
                Palette = palette,
                Children = VisualTreeHelper.GetChildrenCount(activity)
            });

            activity.SetValue(Control.ForegroundProperty, foreground);
        }

        /// <summary>
        /// Подписка на раскладку в режиме кэша: цвет текста ставится <b>синхронно</b> в обработчике
        /// (<see cref="FrameworkElement.LayoutUpdated"/> вызывается до отрисовки кадра), поэтому новая
        /// активность не успевает показаться с чёрной подписью — в отличие от отложенного прохода
        /// <see cref="HookLayout"/>, который и давал видимую перекраску «вторым кадром».
        /// </summary>
        private static void HookTextLayout(FrameworkElement root, StudioThemeColors palette)
        {
            TextHook hook = TextHooks.GetValue(root, key => new TextHook());
            hook.Palette = palette;

            if (hook.Attached)
            {
                return;
            }

            hook.Attached = true;
            root.LayoutUpdated += (sender, args) => ThemeStaleText(root, hook.Palette);
        }

        /// <summary>Подписка на раскладку для режима кэша (цвет текста).</summary>
        private sealed class TextHook
        {
            public StudioThemeColors Palette { get; set; }

            public bool Attached { get; set; }
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
