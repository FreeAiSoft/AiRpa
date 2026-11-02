using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

using System.Windows.Threading;

namespace RPA.Helpers
{
    /// <summary>
    /// Тематизирует нативные WF-контролы (<c>ToolboxControl</c>, <c>OutlineView</c> и т.п.),
    /// которые не подхватывают стили WPF/MaterialDesign приложения, обходом визуального дерева
    /// (этап 6 плана docs\Vs2026_Theme_Plan.md, F-01/F-02).
    /// <para>
    /// В отличие от прежней реализации: значения берутся из <see cref="StudioPalette"/> (не из акцента
    /// MaterialDesign), состояние элемента передаётся триггерами стиля (<c>IsMouseOver</c> → Hover,
    /// <c>IsSelected</c> → Selected), а не разовым присваиванием <c>Foreground</c>, и шрифт — Roboto.
    /// </para>
    /// <para>
    /// Стиль элемента ставится через <see cref="ItemsControl.ItemContainerStyle"/> (наследуется
    /// контейнерами, созданными позже, — важно для виртуализованного Toolbox и для хлебных крошек
    /// дизайнера) и дополнительно на уже материализованные элементы дерева.
    /// </para>
    /// </summary>
    internal static class WfVisualTreeThemer
    {
        private static readonly ConditionalWeakTable<FrameworkElement, object> LoadedHooks =
            new ConditionalWeakTable<FrameworkElement, object>();

        /// <summary>Состояния элементов, стиль которых выбрал WF (hover/выделение палитрой).</summary>
        private static readonly ConditionalWeakTable<TreeViewItem, ItemStateHook> ItemStateHooks =
            new ConditionalWeakTable<TreeViewItem, ItemStateHook>();

        // Кэш «стиль по умолчанию на палитру» отдельный для дерева и для списка: стили разных типов.
        private static readonly ContainerStyleCache TreeItemStyleCache = new ContainerStyleCache();
        private static readonly ContainerStyleCache ListItemStyleCache = new ContainerStyleCache();
        private static readonly ContainerStyleCache ComboBoxItemStyleCache = new ContainerStyleCache();

        /// <summary>Имя части popup-а в шаблоне <see cref="ComboBox"/> (Aero2 и WF-стиль масштаба).</summary>
        private const string ComboBoxPopupPartName = "PART_Popup";

        /// <summary>Внутренние отступы элемента списка комбобокса.</summary>
        private static readonly Thickness ComboBoxItemPadding = new Thickness(4, 2, 4, 2);

        /// <summary>Шаблон элемента списка комбобокса (создаётся один раз на процесс).</summary>
        private static ControlTemplate ComboBoxItemTemplate;

        /// <summary>
        /// Корни, у которых комбобоксов ещё нет (shell bar дизайнера строится шаблоном после
        /// <see cref="FrameworkElement.Loaded"/>): повторный проход по загрузке ставится один раз.
        /// </summary>
        private static readonly ConditionalWeakTable<FrameworkElement, object> ComboBoxSweepHooks =
            new ConditionalWeakTable<FrameworkElement, object>();

        /// <summary>Подписки на раскрытие списка комбобокса: элементы и popup создаются только к этому моменту.</summary>
        private static readonly ConditionalWeakTable<ComboBox, DropDownHook> DropDownHooks =
            new ConditionalWeakTable<ComboBox, DropDownHook>();

        /// <summary>Применяет палитру, определённую по ресурсам приложения (тёмная, если определить не удалось).</summary>
        public static void Apply(DependencyObject root)
        {
            Apply(root, StudioPalette.Detect(Application.Current?.Resources));
        }

        /// <summary>Применяет конкретную палитру к дереву (безопасно вызывать многократно).</summary>
        public static void Apply(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush separator = WfThemeStyles.Brush(palette.Separator);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);

            SetBackground(root, surface);

            if (root is TreeView rootTreeView)
            {
                ThemeTreeView(rootTreeView, palette, surface, separator, foreground);
            }

            foreach (TreeView treeView in FindVisualChildren<TreeView>(root))
            {
                ThemeTreeView(treeView, palette, surface, separator, foreground);
            }

            foreach (TreeViewItem item in FindVisualChildren<TreeViewItem>(root))
            {
                ThemeTreeViewItem(item, palette, foreground);
            }

            foreach (TextBox textBox in FindVisualChildren<TextBox>(root))
            {
                textBox.Background = surface;
                textBox.BorderBrush = separator;
                textBox.Foreground = foreground;
                textBox.CaretBrush = foreground;
                textBox.FontFamily = WfThemeStyles.Font;
            }

            foreach (ScrollBar scrollBar in FindVisualChildren<ScrollBar>(root))
            {
                // Полная замена шаблона скроллбара не делается (см. отклонения в плане):
                // нейтрализуем кисти, которые читает шаблон Aero2.
                scrollBar.Background = surface;
                scrollBar.Foreground = WfThemeStyles.Brush(palette.ForegroundMuted);
                scrollBar.BorderBrush = separator;
            }

            // Комбобоксы (в дизайнере — масштаб в shell bar): шаблон Aero2 рисует под текстом белую
            // плашку, а Foreground наследуется от поверхности — белые буквы на белом фоне.
            ThemeComboBoxes(root, palette);
        }

        /// <summary>
        /// Применяет палитру сразу и повторно — после <see cref="FrameworkElement.Loaded"/>, потому что
        /// у <c>OutlineView</c> дерево строится шаблоном только при загрузке.
        /// Подписка ставится один раз на элемент.
        /// </summary>
        public static void ApplyWhenLoaded(FrameworkElement element)
        {
            ApplyWhenLoaded(element, StudioPalette.Detect(Application.Current?.Resources));
        }

        /// <summary>То же, но с явной палитрой.</summary>
        public static void ApplyWhenLoaded(FrameworkElement element, StudioThemeColors palette)
        {
            if (element == null || palette == null)
            {
                return;
            }

            Apply(element, palette);

            if (LoadedHooks.TryGetValue(element, out _))
            {
                return;
            }

            LoadedHooks.Add(element, LoadedHooks);

            void handler(object sender, RoutedEventArgs args)
            {
                element.Loaded -= handler;
                Apply(element, palette);
            }

            element.Loaded += handler;
        }

        /// <summary>Сколько раз на элемент подписывались через <see cref="ApplyWhenLoaded"/> (для тестов идемпотентности).</summary>
        public static int LoadedHookCount(FrameworkElement element)
        {
            return element == null ? 0 : LoadedHooks.TryGetValue(element, out _) ? 1 : 0;
        }

        /// <summary>
        /// Стиль элемента дерева: нейтральные состояния вместо системного выделения
        /// (<c>SystemColors.HighlightBrushKey</c> до этапа 2 был акцентом MaterialDesign).
        /// </summary>
        internal static Style CreateItemContainerStyle(Style baseStyle, StudioThemeColors palette)
        {
            return CreateContainerStyle(typeof(TreeViewItem), TreeViewItem.IsSelectedProperty, baseStyle, palette, TreeItemStyleCache, null);
        }

        /// <summary>
        /// Стиль элемента списка — хлебные крошки дизайнера (<c>Main › Sequence › Assign</c>).
        /// Системный chrome элемента даёт белую плашку поверх тёмной темы, поэтому фон прозрачный,
        /// состояния — яркостью палитры, рамка погашена.
        /// </summary>
        internal static Style CreateListItemStyle(Style baseStyle, StudioThemeColors palette)
        {
            return CreateContainerStyle(typeof(ListBoxItem), ListBoxItem.IsSelectedProperty, baseStyle, palette, ListItemStyleCache, new Thickness(0));
        }

        /// <summary>
        /// Стиль элемента раскрытого списка комбобокса. Системный chrome шаблона Aero2
        /// (<c>Border#Bd</c>) подсвечивает наведение и выделение светлой плашкой, причём триггерами
        /// шаблона — сеттеры стиля их не перекрывают: в тёмной теме текст на такой плашке не читается.
        /// Поэтому элемент получает собственный шаблон, а состояния рисуются триггерами палитры.
        /// </summary>
        internal static Style CreateComboBoxItemStyle(Style baseStyle, StudioThemeColors palette)
        {
            return CreateContainerStyle(
                typeof(ComboBoxItem),
                ListBoxItem.IsSelectedProperty,
                baseStyle,
                palette,
                ComboBoxItemStyleCache,
                new Thickness(0),
                CreateComboBoxItemTemplate());
        }

        /// <summary>
        /// Темизирует комбобоксы поддерева (в дизайнере — масштаб в shell bar, <c>zoomPicker</c>).
        /// <para>
        /// Регресс F-04: свёрнутый комбобокс масштаба показывал белые буквы на белом фоне. WF-стиль
        /// <c>statusBarComboBoxStyle</c> задаёт только шрифт, <c>Foreground</c> и прозрачный фон, а шаблон
        /// Aero2 рисует под текстом опаковую белую плашку (<c>Border#templateRoot</c> кнопки раскрытия и
        /// <c>Border#Border</c> редактируемой части). Текст наследует <c>Foreground</c> shell bar, то есть
        /// в тёмной теме он светлый — на белой плашке его не видно.
        /// </para>
        /// <para>
        /// Корень, у которого комбобоксов ещё нет, получает повторный проход по
        /// <see cref="FrameworkElement.Loaded"/>: содержимое shell bar дизайнера материализуется генератором
        /// только после применения шаблона.
        /// </para>
        /// </summary>
        internal static void ThemeComboBoxes(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            if (!SweepComboBoxes(root, palette))
            {
                HookComboBoxSweep(root, palette);
            }
        }

        /// <summary>Темизирует корень-комбобокс и все комбобоксы поддерева; <c>true</c> — если хоть один найден.</summary>
        private static bool SweepComboBoxes(DependencyObject root, StudioThemeColors palette)
        {
            bool themed = false;

            if (root is ComboBox rootComboBox)
            {
                ThemeComboBox(rootComboBox, palette);
                themed = true;
            }

            foreach (ComboBox comboBox in FindVisualChildren<ComboBox>(root))
            {
                ThemeComboBox(comboBox, palette);
                themed = true;
            }

            return themed;
        }

        /// <summary>
        /// Палитра комбобокса: собственные кисти, редактируемая часть, плашки шаблона Aero2, стиль элементов
        /// списка и popup. Стили WF при этом не подменяются — светлые кисти шаблона гасятся по эффективным
        /// значениям, как и в остальных WF-контролах.
        /// </summary>
        private static void ThemeComboBox(ComboBox comboBox, StudioThemeColors palette)
        {
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);
            SolidColorBrush separator = WfThemeStyles.Brush(palette.Separator);

            comboBox.Background = surface;
            comboBox.Foreground = foreground;
            comboBox.BorderBrush = separator;
            comboBox.FontFamily = WfThemeStyles.Font;
            comboBox.FontSize = WfThemeStyles.FontSize;

            // Редактируемая часть (масштаб в shell bar — IsEditable) и есть видимый свёрнутый текст.
            foreach (TextBox textBox in FindVisualChildren<TextBox>(comboBox))
            {
                textBox.Background = surface;
                textBox.Foreground = foreground;
                textBox.BorderBrush = separator;
                textBox.CaretBrush = foreground;
                textBox.FontFamily = WfThemeStyles.Font;
                textBox.FontSize = WfThemeStyles.FontSize;
            }

            // Белые плашки шаблона гасятся к поверхности, стрелка раскрытия получает ForegroundMuted
            // (системно-серую стрелку на тёмной поверхности не видно).
            WfThemeStyles.NeutralizeLightBrushes(comboBox, palette, stopAtItemContainers: true);

            // Чистый белый — токен палитры (CloseHoverForeground), поэтому общий проход его не трогает,
            // а внутри комбобокса белая площадь — это всегда плашка шаблона Aero2 под текстом.
            PaintTemplatePlates(comboBox, palette);

            // Контейнеры списка наследуют стиль: генератор создаёт их только при раскрытии.
            if (comboBox.ItemContainerStyleSelector == null)
            {
                comboBox.ItemContainerStyle = CreateComboBoxItemStyle(comboBox.ItemContainerStyle, palette);
            }

            ThemeComboBoxDropDown(comboBox, palette);
            HookComboBoxDropDown(comboBox, palette);
        }

        /// <summary>
        /// Список раскрытого комбобокса живёт в popup — вне визуального дерева комбобокса, поэтому в общий
        /// проход по поддереву не попадает. Непрозрачные белые плашки (<c>Border#DropDownBorder</c>,
        /// <c>Rectangle#OpaqueRect</c>), светлые скроллбары и элементы гасятся к палитре.
        /// </summary>
        private static void ThemeComboBoxDropDown(ComboBox comboBox, StudioThemeColors palette)
        {
            if (!(comboBox?.Template?.FindName(ComboBoxPopupPartName, comboBox) is Popup popup)
                || !(popup.Child is DependencyObject content))
            {
                return;
            }

            WfThemeStyles.NeutralizeLightBrushes(content, palette);

            // Белые плашки шаблона списка (Border#DropDownBorder, Rectangle#OpaqueRect, полосы прокрутки):
            // общий проход не трогает чистый белый — это токен палитры CloseHoverForeground.
            PaintTemplatePlates(content, palette);
        }

        /// <summary>
        /// Поправка списка при каждом раскрытии: контейнеры и popup создаются генератором к этому моменту,
        /// а палитра могла смениться после первого раскрытия. Подписка ставится одна на комбобокс.
        /// </summary>
        private static void HookComboBoxDropDown(ComboBox comboBox, StudioThemeColors palette)
        {
            if (DropDownHooks.TryGetValue(comboBox, out DropDownHook hook))
            {
                hook.Palette = palette;
                return;
            }

            hook = new DropDownHook { Palette = palette };
            DropDownHooks.Add(comboBox, hook);

            comboBox.DropDownOpened += (sender, args) =>
            {
                ThemeComboBoxDropDown(comboBox, hook.Palette);

                // Шаблон popup-а и полосы прокрутки применяются при раскладке списка, уже после события:
                // один отложенный проход, иначе светлые плашки шаблона остаются на месте.
                _ = comboBox.Dispatcher?.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => ThemeComboBoxDropDown(comboBox, hook.Palette)));
            };
        }

        /// <summary>
        /// Повторный проход по <see cref="FrameworkElement.Loaded"/> для корня, у которого комбобоксов пока
        /// нет (shell bar дизайнера строится шаблоном). Подписка ставится один раз на элемент.
        /// </summary>
        private static void HookComboBoxSweep(DependencyObject root, StudioThemeColors palette)
        {
            if (!(root is FrameworkElement element) || ComboBoxSweepHooks.TryGetValue(element, out _))
            {
                return;
            }

            ComboBoxSweepHooks.Add(element, ComboBoxSweepHooks);

            void sweep(object sender, RoutedEventArgs args)
            {
                element.Loaded -= sweep;
                ComboBoxSweepHooks.Remove(element);

                if (!SweepComboBoxes(element, palette) && element.Dispatcher != null)
                {
                    // StatusBar наполняется на этапе Loaded: один отложенный повтор на случай, если
                    // шаблон применился уже после события.
                    _ = element.Dispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        new Action(() => SweepComboBoxes(element, palette)));
                }
            }

            if (element.IsLoaded)
            {
                // Loaded уже прошёл — повторного события не будет.
                sweep(element, null);
                return;
            }

            element.Loaded += sweep;
        }

        /// <summary>
        /// Шаблон элемента списка: рамка + <see cref="ContentPresenter"/>. Шаблон Aero2 красит подсветку
        /// собственным <c>Border#Bd</c> светлыми плашками, поэтому элемент рисуется этим шаблоном, а цвета
        /// состояний приходят сеттерами и триггерами стиля (палитра).
        /// </summary>
        private static ControlTemplate CreateComboBoxItemTemplate()
        {
            if (ComboBoxItemTemplate != null)
            {
                return ComboBoxItemTemplate;
            }

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, ComboBoxItemPadding);
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            ControlTemplate template = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border };
            template.Seal();

            ComboBoxItemTemplate = template;
            return template;
        }



        /// <summary>
        /// Общая часть стилей контейнеров: существующий стиль WF оборачивается через
        /// <see cref="WfThemeStyles.Wrap"/> (шаблон элемента сохраняется), состояния — триггеры.
        /// </summary>
        private static Style CreateContainerStyle(
            Type targetType,
            DependencyProperty selectedProperty,
            Style baseStyle,
            StudioThemeColors palette,
            ContainerStyleCache cache,
            Thickness? borderThickness,
            ControlTemplate template = null)
        {
            if (palette == null)
            {
                return baseStyle;
            }

            if (baseStyle != null && WfThemeStyles.IsThemed(baseStyle))
            {
                if (ReferenceEquals(WfThemeStyles.PaletteOf(baseStyle), palette))
                {
                    // Та же палитра: стиль переиспользуется, цепочка BasedOn не растёт.
                    return baseStyle;
                }

                // Палитра сменилась: в сеттерах остались кисти прежней темы, поэтому обёртка
                // пересоздаётся от исходного стиля WF — его шаблон элемента сохраняется.
                baseStyle = UnwrapThemed(baseStyle);
            }

            if (baseStyle != null && baseStyle.TargetType != null && !baseStyle.TargetType.IsAssignableFrom(targetType))
            {
                // Стиль с чужим TargetType (наследник элемента) обернуть нельзя: WPF бросит
                // на несовпадении TargetType, а подмена стиля потеряла бы шаблон элемента WF.
                return baseStyle;
            }

            if (baseStyle == null && cache.Style != null && ReferenceEquals(cache.Palette, palette))
            {
                return cache.Style;
            }

            Style style = WfThemeStyles.Wrap(targetType, baseStyle);

            style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));

            if (baseStyle == null && template != null)
            {
                // Собственный шаблон ставится только когда стиля WF нет: иначе шаблон элемента WF
                // (и его обработчики) потерялся бы вместе с BasedOn-цепочкой.
                style.Setters.Add(new Setter(Control.TemplateProperty, template));
            }

            if (borderThickness.HasValue)
            {
                style.Setters.Add(new Setter(Control.BorderThicknessProperty, borderThickness.Value));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            }

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Hover)));
            style.Triggers.Add(hover);

            // Триггер выделения добавлен последним: при одновременном hover он выигрывает.
            Trigger selected = new Trigger { Property = selectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Selected)));
            style.Triggers.Add(selected);

            WfThemeStyles.MarkThemed(style, palette);

            if (baseStyle == null)
            {
                // Элементы без собственного стиля переиспользуют один экземпляр (нет роста числа стилей).
                cache.Style = style;
                cache.Palette = palette;
            }

            return style;
        }

        private static void ThemeTreeView(TreeView treeView, StudioThemeColors palette, Brush surface, Brush separator, Brush foreground)
        {
            treeView.Background = surface;
            treeView.BorderBrush = separator;
            treeView.Foreground = foreground;
            treeView.FontFamily = WfThemeStyles.Font;

            // Стили элементов, выбранные StyleSelector'ом (ToolboxControl и его ToolItemStyle/
            // CategoryItemStyle), подменять нельзя: ItemContainerStyle перекрывает работу селектора,
            // а вместе со стилем WF теряются шаблон элемента и обработчики — перетаскивание активности
            // из контрола активностей в дизайнер перестаёт начинаться. Такие элементы получают
            // палитру свойствами (Foreground/FontFamily) и собственными триггерами WF.
            if (treeView.ItemContainerStyleSelector == null)
            {
                treeView.ItemContainerStyle = CreateItemContainerStyle(treeView.ItemContainerStyle, palette);
            }
        }

        private static void ThemeTreeViewItem(TreeViewItem item, StudioThemeColors palette, Brush foreground)
        {
            item.Foreground = foreground;
            item.FontFamily = WfThemeStyles.Font;

            // Стиль элемента выбрал селектор WF (ToolboxControl) — подменять его нельзя, иначе вместе
            // с шаблоном элемента теряются обработчики перетаскивания активности в дизайнер.
            bool wfStyle = item.Style != null && !WfThemeStyles.IsThemed(item.Style);

            if (item.Style == null || WfThemeStyles.IsThemed(item.Style))
            {
                // Уже темизированные элементы переиспользуют готовый стиль: цепочка BasedOn не растёт.
                item.Style = CreateItemContainerStyle(item.Style, palette);
            }

            if (wfStyle)
            {
                // Состояние элемента WF рисует системной темой (Aero2: светло-жёлтая плашка #FFE8A6
                // на hover и выделение), своих триггеров палитры у элемента нет. Подписка ставится
                // до прохода по кистям: фон элемента уже из палитры, и шаблон (TemplateBinding) берёт
                // готовый цвет вместо светлого #E9ECEE.
                HookItemStates(item, palette);
            }

            // Плашка заголовка категории: WF-стиль категории задаёт контейнеру светлый Background (#E9ECEE),
            // а шаблон элемента повторяет его через TemplateBinding (Border#Bd) — в тёмной теме под
            // названием категории остаётся белый прямоугольник. Стиль подменять нельзя, поэтому светлые
            // кисти гасятся по эффективным значениям; вложенные контейнеры получают свой проход.
            WfThemeStyles.NeutralizeLightBrushes(item, palette, stopAtItemContainers: true);
        }

        /// <summary>
        /// Гасит светлые плашки шаблона внутри комбобокса: опаковую белую площадь под текстом и в кнопке
        /// раскрытия (<c>Border#templateRoot</c>, <c>Border#Border</c> шаблона Aero2), белую подложку списка
        /// (<c>Border#DropDownBorder</c>, <c>Rectangle#OpaqueRect</c>) и светлые полосы прокрутки.
        /// <para>
        /// Общий проход <see cref="WfThemeStyles.NeutralizeLightBrushes"/> такие кисти не трогает: чистый
        /// белый объявлен токеном палитры (<c>CloseHoverForeground</c> — текст крестика закрытия). Здесь
        /// плашка узнаётся иначе: цвет сопоставляется с токеном палитры прежней темы (так же, как это
        /// делает <see cref="WfThemeStyles.TryMapStalePaletteColour"/>), поэтому белая плашка Aero2
        /// перекрашивается в поверхность тёмной темы, а тёмная — обратно в светлую. Стиль WF при этом
        /// не подменяется: гасятся значения свойств, а не шаблон.
        /// </para>
        /// </summary>
        private static void PaintTemplatePlates(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            double surfaceLuminance = StudioPalette.Luminance(palette.Surface);
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            HashSet<Color> paletteTokens = new HashSet<Color>(palette.Tokens.Values);

            // Чистый белый объявлен в палитре как CloseHoverForeground (текст крестика закрытия): плашкой
            // он быть не может, поэтому внутри комбобокса такой цвет — всегда артефакт шаблона Aero2.
            paletteTokens.Remove(palette.CloseHoverForeground);
            Stack<DependencyObject> pending = new Stack<DependencyObject>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                DependencyObject element = pending.Pop();

                if (element is Panel panel)
                {
                    panel.Background = PlateOrPalette(panel.Background, palette, paletteTokens, surfaceLuminance, surface);
                }
                else if (element is Control control)
                {
                    control.Background = PlateOrPalette(control.Background, palette, paletteTokens, surfaceLuminance, surface);
                }
                else if (element is Border border)
                {
                    border.Background = PlateOrPalette(border.Background, palette, paletteTokens, surfaceLuminance, surface);
                }
                else if (element is Shape shape)
                {
                    shape.Fill = PlateOrPalette(shape.Fill, palette, paletteTokens, surfaceLuminance, surface);
                    shape.Stroke = PlateOrPalette(shape.Stroke, palette, paletteTokens, surfaceLuminance, surface);
                }

                foreach (DependencyObject child in XamlHelper.Children(element))
                {
                    if (child is ComboBoxItem || child is TreeViewItem || child is ListBoxItem)
                    {
                        // У контейнеров списка свой проход (стиль палитры), внутрь не заходим.
                        continue;
                    }

                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Кисть плашки: токен палитры прежней темы заменяется тем же токеном активной (белая плашка шаблона
        /// Aero2 — токен светлой поверхности), а светлая непрозрачная площадь гасится к поверхности. Кисти
        /// активной палитры остаются как есть — это собственная работа темизатора.
        /// </summary>
        private static Brush PlateOrPalette(
            Brush brush,
            StudioThemeColors palette,
            HashSet<Color> paletteTokens,
            double surfaceLuminance,
            SolidColorBrush surface)
        {
            if (WfThemeStyles.TryMapStalePaletteColour(brush, palette, out Color mapped))
            {
                return WfThemeStyles.Brush(mapped);
            }

            if (brush is SolidColorBrush solid
                && solid.Color.A == 0xFF
                && !paletteTokens.Contains(solid.Color)
                && StudioPalette.Luminance(solid.Color) > surfaceLuminance + WfThemeStyles.LightPlateThreshold)
            {
                return surface;
            }

            return brush;
        }

        /// Исходный стиль WF под обёрткой темизатора: при смене палитры новый стиль строится от него,
        /// а не от стиля прежней темы.
        /// </summary>
        private static Style UnwrapThemed(Style style)
        {
            Style current = style;

            while (current != null && WfThemeStyles.IsThemed(current))
            {
                current = current.BasedOn;
            }

            return current;
        }

        private static void SetBackground(DependencyObject element, Brush brush)
        {
            if (element is Control control)
            {
                control.Background = brush;
            }
            else if (element is Panel panel)
            {
                panel.Background = brush;
            }
            else if (element is Border border)
            {
                border.Background = brush;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            // Обход через XamlHelper: он пропускает не-визуальные узлы (RowDefinition/ColumnDefinition)
            // вместо ArgumentException.
            return XamlHelper.FindVisualChildren<T>(parent);
        }

        /// <summary>
        /// Hover и выделение элемента, стиль которого выбрал WF: триггеров палитры в таком стиле нет, а
        /// системная тема рисует состояние светлой плашкой (#FFE8A6) — на тёмном фоне это белый фон под
        /// названием. Поэтому состояния перекрашиваются значениями палитры, а стиль WF остаётся нетронутым.
        /// Подписка ставится один раз на элемент; повторное применение темы только обновляет палитру.
        /// </summary>
        private static void HookItemStates(TreeViewItem item, StudioThemeColors palette)
        {
            ItemStateHook hook = ItemStateHooks.GetValue(item, key => new ItemStateHook());
            hook.Palette = palette;

            // Рамку элемента рисует системная тема (жёлтая #E5C365 на выделении) — гасим один раз:
            // локальное значение сильнее триггеров стиля и шаблона.
            item.BorderBrush = Brushes.Transparent;

            // Непрозрачный фон элемента — это плашка (заголовок категории): в нейтральном состоянии она
            // равна поверхности палитры. Прозрачный фон — строка активности: фон рисует шаблон WF.
            hook.Base = item.Background is SolidColorBrush solid && solid.Color.A != 0
                ? WfThemeStyles.Brush(palette.Surface)
                : Brushes.Transparent;

            if (hook.Subscribed)
            {
                // Палитра сменилась — перекрашиваем сразу, не дожидаясь смены состояния.
                hook.Apply(item);
                return;
            }

            hook.Subscribed = true;
            hook.Handler = (sender, args) => hook.Apply(item);
            hook.SelectedDescriptor = DependencyPropertyDescriptor.FromProperty(
                TreeViewItem.IsSelectedProperty, typeof(TreeViewItem));
            hook.HoverDescriptor = DependencyPropertyDescriptor.FromProperty(
                UIElement.IsMouseOverProperty, typeof(TreeViewItem));

            hook.SelectedDescriptor?.AddValueChanged(item, hook.Handler);
            hook.HoverDescriptor?.AddValueChanged(item, hook.Handler);

            // Подписки снимаются вместе с элементом: генератор контейнеров пересоздаёт элементы при
            // перестроении категорий, и обработчики не должны накапливаться.
            item.Unloaded += (sender, args) => UnhookItemStates(item);

            hook.Apply(item);
        }


        /// <summary>Снимает подписки на состояния элемента (повторная темизация подпишет заново).</summary>
        private static void UnhookItemStates(TreeViewItem item)
        {
            if (!ItemStateHooks.TryGetValue(item, out ItemStateHook hook) || !hook.Subscribed)
            {
                return;
            }

            hook.Subscribed = false;
            hook.SelectedDescriptor?.RemoveValueChanged(item, hook.Handler);
            hook.HoverDescriptor?.RemoveValueChanged(item, hook.Handler);
        }


        /// <summary>Подписка на раскрытие списка комбобокса и палитра, которой он рисуется.</summary>
        private sealed class DropDownHook
        {
            public StudioThemeColors Palette { get; set; }
        }

        /// <summary>Стиль контейнера по умолчанию для одной палитры (чтобы не плодить экземпляры).</summary>
        private sealed class ContainerStyleCache
        {
            public Style Style { get; set; }

            public StudioThemeColors Palette { get; set; }
        }

        /// <summary>Подписка на hover/выделение элемента и палитра, которой они рисуются.</summary>
        private sealed class ItemStateHook
        {
            public StudioThemeColors Palette { get; set; }

            /// <summary>Фон нейтрального состояния (поверхность палитры или прозрачный).</summary>
            public Brush Base { get; set; }

            public bool Subscribed { get; set; }

            public DependencyPropertyDescriptor SelectedDescriptor { get; set; }

            public DependencyPropertyDescriptor HoverDescriptor { get; set; }

            public EventHandler Handler { get; set; }

            /// <summary>Текущее состояние элемента: выделение сильнее hover (как в стиле контейнера).</summary>
            public void Apply(TreeViewItem item)
            {
                if (item == null || Palette == null)
                {
                    return;
                }

                item.Background = item.IsSelected
                    ? WfThemeStyles.Brush(Palette.Selected)
                    : item.IsMouseOver
                        ? WfThemeStyles.Brush(Palette.Hover)
                        : Base;

                // Шаблон WF перекрашивает состояние собственными триггерами светлыми кистями
                // (#F0F0F0 плашка, #E5C365 рамка) — гасим их сразу после смены состояния.
                WfThemeStyles.NeutralizeLightBrushes(item, Palette, stopAtItemContainers: true);
            }
        }
    }
}
