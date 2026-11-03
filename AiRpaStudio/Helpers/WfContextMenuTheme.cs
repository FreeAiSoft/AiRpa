using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RPA.Helpers
{
    /// <summary>
    /// Оформление контекстных меню дизайнера: собственные шаблон меню, панель пунктов и стиль контейнера
    /// пункта — на палитре <see cref="StudioPalette"/>.
    /// <para>
    /// Почему локальных <c>Background</c>/<c>Foreground</c> недостаточно (регресс «белое меню с белыми
    /// буквами» в таблицах «Переменные»/«Аргументы»):
    /// </para>
    /// <list type="number">
    /// <item>Меню живёт в popup — вне визуального дерева дизайнера, поэтому ни один проход темы
    /// (<see cref="WfDesignerTheme.Apply"/>, <see cref="WfVisualTreeThemer"/>) до него не доезжает: popup
    /// не является потомком <c>DesignerView</c>.</item>
    /// <item>Рисуют меню чужие шаблоны: в <c>App.xaml</c> подключён <c>MaterialDesign2.Defaults.xaml</c> с
    /// неявными стилями <c>ContextMenu</c>/<c>MenuItem</c>, а в чистом процессе — системный Aero2. Их
    /// подсветка строится от <c>Foreground</c> (полупрозрачная светлая накладка на тёмной теме), углы,
    /// рамку и тень рисует шаблон, а не наши кисти.</item>
    /// </list>
    /// <para>
    /// Отсюда две точки правды: <see cref="Apply"/> (собственные шаблоны + кисти палитры, вызывается
    /// фабрикой <see cref="WfDesignerTheme.CreateEditingContextMenu"/> и перекраской
    /// <see cref="WfDesignerTheme.RecolorEditingContextMenu"/>) и <see cref="Hook"/> — общий проход для
    /// меню, созданных не нами (контекстные меню самого WF-дизайнера). Проход ставится на класс
    /// <see cref="ContextMenu"/> и срабатывает по <see cref="ContextMenu.OpenedEvent"/>, но
    /// применяется только к меню, чья цепочка родителей доходит до поверхности дизайнера: меню
    /// AvalonDock, дерева проекта и чата живут на своих стилях.
    /// </para>
    /// <para>
    /// Цвета берутся только из <see cref="StudioPalette"/>: поверхность <c>Surface</c>, hairline
    /// <c>SeparatorStrong</c>, подсветка <c>Hover</c>, недоступный пункт <c>ForegroundMuted</c>. Тень
    /// (<see cref="ContextMenu.HasDropShadow"/>) выключена: она даёт светлый прямоугольник поверх тёмной
    /// темы. Шаблоны кэшируются по палитре, поэтому повторное применение не пересоздаёт дерево.
    /// </para>
    /// </summary>
    internal static class WfContextMenuTheme
    {
        /// <summary>Имя корневой рамки меню в шаблоне (по нему части шаблона находят тесты и диагностика).</summary>
        internal const string MenuBorderPartName = "StudioContextMenuBorder";

        /// <summary>Имя рамки пункта в шаблоне пункта.</summary>
        internal const string MenuItemBorderPartName = "StudioMenuItemBorder";

        /// <summary>Часть шаблона пункта, в которую <see cref="MenuItem"/> помещает подменю.</summary>
        private const string MenuItemPopupPartName = "PART_Popup";

        /// <summary>Внутренние отступы рамки меню (пункты не касаются hairline).</summary>
        private const double MenuPadding = 4d;

        /// <summary>Горизонтальный отступ пункта: текст не липнет к краю меню.</summary>
        private const double ItemPaddingHorizontal = 12d;

        /// <summary>Вертикальный отступ пункта.</summary>
        private const double ItemPaddingVertical = 6d;

        /// <summary>Отступ текста жеста клавиатуры от заголовка.</summary>
        private const double GestureMargin = 24d;

        /// <summary>Шаблоны на палитру: заморожены и переиспользуются между меню и сменами темы.</summary>
        private static readonly ConditionalWeakTable<StudioThemeColors, MenuTheme> ThemeCache =
            new ConditionalWeakTable<StudioThemeColors, MenuTheme>();

        /// <summary>Палитра, которой уже оформлено меню (повторное открытие не пересобирает шаблоны).</summary>
        private static readonly ConditionalWeakTable<ContextMenu, PaletteMark> Applied =
            new ConditionalWeakTable<ContextMenu, PaletteMark>();

        private static bool _hooked;

        /// <summary>Общий проход по меню дизайнера подключён (для тестов и диагностики).</summary>
        internal static bool IsHooked => _hooked;

        /// <summary>
        /// Ставит меню на палитру: собственный шаблон меню, панель пунктов и стиль контейнера пункта.
        /// Безопасно вызывать многократно (фабрика, каждое открытие, смена темы).
        /// </summary>
        internal static void Apply(ContextMenu menu, StudioThemeColors palette, bool explicitPalette = true)
        {
            if (menu == null)
            {
                return;
            }

            StudioThemeColors colors = palette ?? StudioPalette.Dark;
            MenuTheme theme = ThemeCache.GetValue(colors, key => new MenuTheme(key));

            menu.Background = WfThemeStyles.Brush(colors.Surface);
            menu.Foreground = WfThemeStyles.Brush(colors.Foreground);
            menu.BorderBrush = WfThemeStyles.Brush(colors.SeparatorStrong);
            menu.BorderThickness = new Thickness(1);
            menu.FontFamily = WfThemeStyles.Font;
            menu.FontSize = WfThemeStyles.FontSize;

            // Тень даёт светлый прямоугольник поверх тёмной темы: меню приподнято hairline-рамкой, как
            // остальные поверхности студии.
            menu.HasDropShadow = false;

            menu.Template = theme.MenuTemplate;
            menu.ItemsPanel = theme.ItemsPanel;

            Style itemStyle = theme.ItemStyle;

            // ItemContainerStyle на меню ставить нельзя: ItemsControl применяет его к каждому
            // контейнеру, а Separator — свой контейнер, и стиль с TargetType=MenuItem роняет открытие
            // меню (InvalidOperationException «Стиль … не может применяться к …»). Поэтому стиль
            // раздаётся элементам, а для контейнеров, созданных генератором, работает селектор.
            menu.ItemContainerStyle = null;
            menu.ItemContainerStyleSelector = theme.ItemSelector;

            foreach (object item in menu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (!ReferenceEquals(menuItem.Style, itemStyle))
                    {
                        menuItem.Style = itemStyle;
                    }

                    continue;
                }

                if (item is Separator separator && !ReferenceEquals(separator.Style, theme.SeparatorStyle))
                {
                    separator.Style = theme.SeparatorStyle;
                }
            }

            Mark(menu, colors, explicitPalette);
        }

        /// <summary>
        /// Доводит до палитры уже материализованное меню: светлые плашки чужого шаблона (если они всё-таки
        /// пролезли внутрь) гасятся к поверхности, текст остаётся читаемым. Вызывается из общего прохода
        /// после того, как меню открылось и шаблон применён.
        /// </summary>
        internal static void PaintMaterialized(ContextMenu menu, StudioThemeColors palette)
        {
            if (menu == null || palette == null)
            {
                return;
            }

            WfThemeStyles.NeutralizeLightBrushes(menu, palette, stopAtItemContainers: true);

            // Текст пунктов наследуется от стиля, поэтому здесь только верхнеуровневая страховка:
            // чужой шаблон мог оставить локальный светлый Foreground на самом меню.
            if (menu.ReadLocalValue(Control.ForegroundProperty) is SolidColorBrush foreground
                && !IsPaletteColor(palette, foreground.Color))
            {
                menu.Foreground = WfThemeStyles.Brush(palette.Foreground);
            }
        }

        /// <summary>
        /// Подключает общий проход по контекстным меню поверхности дизайнера. Идемпотентно: ставится один
        /// раз на процесс (старт приложения и каждая смена темы — один и тот же вызов).
        /// </summary>
        internal static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;

            // Меню самого WF открывается правым кликом: ContextMenuOpening приходит ДО показа, поэтому
            // шаблоны успевают стать нашими до первой раскладки popup. Это важно для меню с чужим
            // ItemContainerStyle и разделителями: генератор контейнеров падает на несовпадении
            // TargetType, если стиль ещё чужой (см. Apply — свой стиль раздаётся селектором).
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                ContextMenuService.ContextMenuOpeningEvent,
                new ContextMenuEventHandler(OnMenuOpening),
                handledEventsToo: true);

            // Меню, открытые программно (IsOpen = true), ContextMenuOpening не поднимают: их добирает
            // проход по Opened — уже после показа, но до отрисовки содержимого.
            EventManager.RegisterClassHandler(
                typeof(ContextMenu),
                ContextMenu.OpenedEvent,
                new RoutedEventHandler(OnMenuOpened),
                handledEventsToo: true);
        }

        /// <summary>Меню дизайнера темизируется до показа: правый клик поднимает событие раньше popup.</summary>
        private static void OnMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is FrameworkElement target)
            {
                PrepareOpen(target.ContextMenu, target);
            }
        }

        /// <summary>
        /// Готовит меню дизайнера к открытию: шаблоны и стили ставятся до первой раскладки popup.
        /// Нужно для меню с чужим <see cref="ItemsControl.ItemContainerStyle"/> и разделителями: генератор
        /// контейнеров падает на несовпадении <see cref="Style.TargetType"/>, если стиль ещё чужой.
        /// </summary>
        internal static void PrepareOpen(ContextMenu menu, DependencyObject target)
        {
            if (menu == null || target == null || !IsDesignerElement(target))
            {
                return;
            }

            Apply(menu, StudioPalette.Active, explicitPalette: false);
        }

        /// <summary>Подключён ли к меню общий проход (по какой палитре оформлено).</summary>
        internal static StudioThemeColors PaletteOf(ContextMenu menu)
        {
            return menu != null && Applied.TryGetValue(menu, out PaletteMark mark) ? mark.Palette : null;
        }

        /// <summary>
        /// Общий проход по контекстному меню поверхности дизайнера. Подписка — на класс
        /// <see cref="ContextMenu"/> по <see cref="ContextMenu.OpenedEvent"/>.
        /// <para>
        /// Почему не <see cref="FrameworkElement.LoadedEvent"/>: меню — ребёнок popup, и его
        /// <c>Loaded</c> не доставляется обработчикам класса (проверено: элемент загружен, обработчик
        /// класса не вызван). <c>Opened</c> приходит на каждое открытие, до отрисовки — меню сразу
        /// рисуется шаблоном палитры, без вспышки чужого стиля.
        /// </para>
        /// </summary>
        private static void OnMenuOpened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ContextMenu menu) || !IsOnDesignerSurface(menu))
            {
                return;
            }

            StudioThemeColors palette = StudioPalette.Active;

            if (Applied.TryGetValue(menu, out PaletteMark mark)
                && mark.ExplicitPalette
                && !ReferenceEquals(mark.Palette, palette))
            {
                // Палитру этого меню задал его владелец (фабрика и перекраска сервиса): общий проход её
                // не перебивает, а только доводит материализованные части до той же палитры.
                SchedulePaint(menu, mark.Palette);
                return;
            }

            Apply(menu, palette, explicitPalette: false);
            SchedulePaint(menu, palette);
        }

        /// <summary>
        /// Гашение светлых плашек чужого шаблона следующим проходом диспетчера: шаблон применяется к
        /// popup-дереву асинхронно, и в момент открытия материализованных частей ещё нет.
        /// </summary>
        private static void SchedulePaint(ContextMenu menu, StudioThemeColors palette)
        {
            Dispatcher dispatcher = menu?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => PaintMaterialized(menu, palette)));
        }

        /// <summary>
        /// Меню принадлежит поверхности дизайнера: либо его <see cref="ContextMenu.PlacementTarget"/>
        /// (элемент, на котором меню открыли), либо цепочка его собственных родителей доходит до
        /// <c>DesignerView</c> или до контрола WF.
        /// </summary>
        internal static bool IsOnDesignerSurface(ContextMenu menu)
        {
            return IsDesignerElement(menu.PlacementTarget) || IsDesignerElement(menu);
        }

        private static bool IsDesignerElement(DependencyObject element)
        {
            for (DependencyObject current = element; current != null; current = ParentOf(current))
            {
                if (current is FrameworkElement named
                    && string.Equals(named.Name, WfDesignerTheme.DesignerViewElementName, StringComparison.Ordinal))
                {
                    return true;
                }

                string type = current.GetType().FullName ?? string.Empty;

                // Контролы WF (DesignerView, WorkflowViewElement, PropertyInspectorView, редакторы значений)
                // объявлены в System.Activities.Presentation — их меню принадлежат дизайнеру.
                if (type.StartsWith("System.Activities.Presentation", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static DependencyObject ParentOf(DependencyObject element)
        {
            if (element == null)
            {
                return null;
            }

            DependencyObject parent = LogicalTreeHelper.GetParent(element);
            if (parent == null && element is Visual)
            {
                parent = VisualTreeHelper.GetParent(element);
            }

            return parent;
        }

        private static void Mark(ContextMenu menu, StudioThemeColors palette, bool explicitPalette)
        {
            if (Applied.TryGetValue(menu, out PaletteMark mark))
            {
                mark.Palette = palette;
                mark.ExplicitPalette |= explicitPalette;
                return;
            }

            Applied.Add(menu, new PaletteMark { Palette = palette, ExplicitPalette = explicitPalette });
        }

        private static bool IsPaletteColor(StudioThemeColors palette, Color color)
        {
            foreach (Color token in palette.Tokens.Values)
            {
                if (token == color)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Шаблон меню: рамка палитры с hairline и <see cref="ItemsPresenter"/> внутри.</summary>
        private static ControlTemplate CreateMenuTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border), MenuBorderPartName);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(StudioPalette.MenuCornerRadius));
            border.SetValue(Border.PaddingProperty, new Thickness(MenuPadding));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            presenter.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
            template.Seal();
            return template;
        }

        /// <summary>
        /// Панель пунктов: вертикальный <see cref="StackPanel"/> с <c>IsItemsHost</c>. Без собственной
        /// панели <see cref="ItemsPresenter"/> в нашем шаблоне не рисует пункты вовсе (панель приходит из
        /// стиля чужого словаря).
        /// </summary>
        private static ItemsPanelTemplate CreateItemsPanel()
        {
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            panel.SetValue(Panel.IsItemsHostProperty, true);

            ItemsPanelTemplate template = new ItemsPanelTemplate(panel);
            template.Seal();
            return template;
        }

        /// <summary>
        /// Стиль контейнера пункта: собственный шаблон (рамка палитры + заголовок + жест + popup подменю),
        /// подсветка <c>Hover</c>, недоступный пункт — <c>ForegroundMuted</c>.
        /// <para>
        /// Собственный шаблон обязателен: в шаблонах MaterialDesign/Aero2 подсветка и «ripple» строятся от
        /// <c>Foreground</c>, поэтому в тёмной теме наведение давало светлую накладку, а текст на ней —
        /// белый по белому.
        /// </para>
        /// </summary>
        private static Style CreateItemStyle(StudioThemeColors palette)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border), MenuItemBorderPartName);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(StudioPalette.TabCornerRadius));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));
            border.AppendChild(grid);

            // Иконка | заголовок | жест клавиатуры — та же раскладка, что у стандартного пункта меню.
            grid.AppendChild(CreateColumn(GridUnitType.Auto));
            grid.AppendChild(CreateColumn(GridUnitType.Star));
            grid.AppendChild(CreateColumn(GridUnitType.Auto));

            FrameworkElementFactory icon = CreatePresenter("Icon", 0);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            grid.AppendChild(icon);

            FrameworkElementFactory header = CreatePresenter("Header", 1);
            header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            header.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            grid.AppendChild(header);

            FrameworkElementFactory gesture = CreatePresenter("InputGestureText", 2);
            gesture.SetValue(FrameworkElement.MarginProperty, new Thickness(GestureMargin, 0, 0, 0));
            gesture.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            gesture.SetValue(TextElement.ForegroundProperty, WfThemeStyles.Brush(palette.ForegroundMuted));
            grid.AppendChild(gesture);

            // Подменю: пункт сам наполняет эту часть шаблона, поэтому пункты с вложенными элементами
            // (например «Удалить переменную» с подтверждением) не потеряют своё поведение.
            FrameworkElementFactory popup = new FrameworkElementFactory(typeof(Popup), MenuItemPopupPartName);
            popup.SetValue(Grid.ColumnProperty, 2);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(
                Popup.IsOpenProperty,
                new TemplateBindingExtension(MenuItem.IsSubmenuOpenProperty));
            grid.AppendChild(popup);

            Style style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, SealItemTemplate(border)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(ItemPaddingHorizontal, ItemPaddingVertical, ItemPaddingHorizontal, ItemPaddingVertical)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));
            style.Setters.Add(new Setter(Control.FontSizeProperty, WfThemeStyles.FontSize));

            // Наведение и подсветка клавиатурой — Hover палитры; шаблоны чужого словаря вместо этого
            // накладывали полупрозрачный Foreground.
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Hover)));
            style.Triggers.Add(hover);

            Trigger highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Hover)));
            style.Triggers.Add(highlighted);

            // Недоступный пункт (в таблице пространств имён ячейки только для чтения) — приглушённый текст:
            // системный светло-серый на тёмной поверхности не читается.
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.ForegroundMuted)));
            style.Triggers.Add(disabled);

            WfThemeStyles.MarkThemed(style, palette);
            return style;
        }

        /// <summary>
        /// Стиль разделителя: hairline <c>Separator</c> высотой 1px. Системный <see cref="Separator"/>
        /// рисует светлую линию, которая на тёмной поверхности выглядит как зазор чужого шаблона.
        /// </summary>
        private static Style CreateSeparatorStyle(StudioThemeColors palette)
        {
            FrameworkElementFactory line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            line.SetValue(FrameworkElement.HeightProperty, 1d);
            line.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4));

            ControlTemplate template = new ControlTemplate(typeof(Separator)) { VisualTree = line };
            template.Seal();

            Style style = new Style(typeof(Separator));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Separator)));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));

            WfThemeStyles.MarkThemed(style, palette);
            return style;
        }

        private static FrameworkElementFactory CreateColumn(GridUnitType type)
        {
            FrameworkElementFactory column = new FrameworkElementFactory(typeof(ColumnDefinition));
            column.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, type));
            return column;
        }

        private static FrameworkElementFactory CreatePresenter(string contentSource, int column)
        {
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, contentSource);
            presenter.SetValue(Grid.ColumnProperty, column);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            return presenter;
        }

        private static ControlTemplate SealItemTemplate(FrameworkElementFactory border)
        {
            ControlTemplate template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
            template.Seal();
            return template;
        }

        /// <summary>Шаблоны одной палитры: создаются один раз и дальше только переиспользуются.</summary>
        private sealed class MenuTheme
        {
            public MenuTheme(StudioThemeColors palette)
            {
                Palette = palette;
                MenuTemplate = CreateMenuTemplate();
                ItemsPanel = CreateItemsPanel();
                ItemStyle = CreateItemStyle(palette);
                SeparatorStyle = CreateSeparatorStyle(palette);
                ItemSelector = new MenuItemStyleSelector(ItemStyle);
            }

            public StudioThemeColors Palette { get; }

            public ControlTemplate MenuTemplate { get; }

            public ItemsPanelTemplate ItemsPanel { get; }

            public Style ItemStyle { get; }

            public Style SeparatorStyle { get; }

            /// <summary>Стиль только для пунктов: <see cref="Separator"/> и прочие контейнеры не трогаются.</summary>
            public StyleSelector ItemSelector { get; }
        }

        /// <summary>
        /// Раздача стиля контейнерам меню. Нужен вместо <see cref="ItemsControl.ItemContainerStyle"/>:
        /// тот применяется к любому контейнеру, включая <see cref="Separator"/>, и падает на несовпадении
        /// <see cref="Style.TargetType"/>.
        /// </summary>
        private sealed class MenuItemStyleSelector : StyleSelector
        {
            private readonly Style _itemStyle;

            public MenuItemStyleSelector(Style itemStyle)
            {
                _itemStyle = itemStyle;
            }

            public override Style SelectStyle(object item, DependencyObject container)
            {
                return item is MenuItem ? _itemStyle : null;
            }
        }

        /// <summary>Палитра, которой оформлено меню (смена темы пересобирает шаблоны).</summary>
        private sealed class PaletteMark
        {
            public StudioThemeColors Palette { get; set; }

            /// <summary>
            /// Палитру задал тот, кто создал меню (фабрика/перекраска сервиса): общий проход по меню
            /// дизайнера её не перебивает — иначе меню, созданное в другой палитре, перекрашивалось бы
            /// «палитрой окружения».
            /// </summary>
            public bool ExplicitPalette { get; set; }
        }
    }
}
