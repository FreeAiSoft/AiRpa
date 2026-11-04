using System;
using System.Activities.Presentation;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Agent.Core.Models;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RPA.Helpers
{
    /// <summary>
    /// Тематизация поверхности дизайнера WF (<c>DesignerView</c>, shell bar, breadcrumb, MiniMap,
    /// адаптеры ошибок, контекстные меню) — этап 7 плана docs\Vs2026_Theme_Plan.md, F-04…F-06.
    /// <para>
    /// Почему не хватает словаря приложения: шаблоны <c>DesignerView</c> тянут цвета из собственных
    /// ресурсов (<c>ColorResources.xaml</c>, слитый в <c>DesignerView.Resources</c>), поэтому словарь
    /// палитры кладётся <b>и</b> в корень документа, <b>и</b> в <c>DesignerView</c> — последним, чтобы его
    /// значения выигрывали у <c>ColorResources.xaml</c>. Часть шаблонов берёт кисти через
    /// <c>{StaticResource}</c> и уже не пересчитывается при смене темы; для них темизатор заменяет
    /// дефолтные цвета WF по значению (см. <see cref="ReplaceWorkflowDefaultColors"/>).
    /// </para>
    /// <para>
    /// Цвета берутся только из <see cref="StudioPalette"/>: один фон <c>Surface</c>, состояния —
    /// яркостью (<c>Hover</c>/<c>Selected</c>), зоны разделяет hairline <c>Separator</c>. Красный
    /// (<c>CloseHoverBackground</c>) используется исключительно для адаптера ошибки.
    /// </para>
    /// </summary>
    internal static class WfDesignerTheme
    {
        /// <summary>Ключ фона поверхности дизайнера (F-05): на него ссылается <c>RoslynExpressionEditor.xaml</c>.</summary>
        public const string DesignerViewBackgroundKey = "DesignerViewBackground";

        /// <summary>Ключ основного текста дизайнера (F-05).</summary>
        public const string DesignerViewForegroundKey = "DesignerViewForeground";

        /// <summary>Имена элементов <c>DesignerView</c> из Reference Source (docs\Reference_Source_Storage.md).</summary>
        public const string DesignerViewElementName = "xnDesignerView";

        public const string ShellBarElementName = "shellBar";
        public const string BreadCrumbElementName = "breadCrumbListBox";
        public const string MiniMapElementName = "miniMap";
        public const string ExtensionSurfaceElementName = "designerExtensionSurface";

        /// <summary>Кнопки «Развернуть все»/«Свернуть все» в шапке <c>DesignerView</c> (имена из Reference Source).</summary>
        public const string ExpandAllButtonName = "expandAllButton";
        public const string CollapseAllButtonName = "collapseAllButton";

        /// <summary>Подпись кнопки в шаблоне WF (<c>ExpandAllCollapseAllTemplate</c>).</summary>
        public const string HeaderCaptionPartName = "collapseAllText";

        /// <summary>Дополнительный отступ пары кнопок от правой кромки дизайнера, px.</summary>
        public const double HeaderButtonsRightInset = 5d;



        /// <summary>Маркер словаря, созданного темизатором (повторное применение обновляет его, а не добавляет новый).</summary>
        private const string PaletteDictionaryMarker = "Studio.WfDesignerTheme.Palette";

        // Дефолтные цвета WF (System.Activities.Presentation\DefaultColorResources.xaml) — нужны как
        // запасной вариант, если сборка WF не отдаёт цвет (например, до инициализации ресурсов).
        private static readonly Color FallbackDesignerViewBackground = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color FallbackViewElementBackground = Color.FromRgb(0xE9, 0xEC, 0xEE);
        private static readonly Color FallbackViewElementBorder = Color.FromRgb(0xD0, 0xD7, 0xE2);
        private static readonly Color FallbackViewElementCaption = Color.FromRgb(0x1B, 0x29, 0x3E);

        /// <summary>Кладёт ключи дизайнера в ресурсы приложения (вызывать при старте и после смены темы).</summary>
        public static void ApplyToApplication()
        {
            ApplyToApplication(Application.Current?.Resources, StudioPalette.Active);
        }

        /// <summary>
        /// Кладёт ключи дизайнера в указанный словарь. Словарь-владелец создаётся один раз и дальше
        /// только мутируется: <c>DynamicResource</c>-ссылки в уже загруженных шаблонах WF пересчитываются
        /// на каждое изменение, а число слитых словарей не растёт.
        /// </summary>
        public static void ApplyToApplication(ResourceDictionary target, StudioThemeColors palette)
        {
            // Контекстные меню живут в popup и в проходы по дереву дизайнера не попадают: общий проход
            // подключается здесь — на старте приложения и на каждой смене темы (идемпотентно).
            WfContextMenuTheme.Hook();

            if (target == null || palette == null)
            {
                return;
            }

            // Связанные кисти словаря WF: смена темы = новое значение источников (без обхода дерева).
            // Вызывается и при старте, и на каждой смене темы — до заполнения словаря приложения.
            WfColorResourcesOverride.ApplyPalette(palette);

            ResourceDictionary theme = GetOrCreatePaletteDictionary(target);
            theme.Clear();

            foreach (KeyValuePair<string, object> pair in CreateResourceValues(palette))
            {
                theme[pair.Key] = pair.Value;
            }

            theme[PaletteDictionaryMarker] = PaletteDictionaryMarker;
        }

        /// <summary>Темизирует поверхность дизайнера (палитра определяется по ресурсам приложения).</summary>
        public static void Apply(FrameworkElement root)
        {
            Apply(root, StudioPalette.Active);
        }

        /// <summary>Темизирует поверхность дизайнера заданной палитрой (безопасно вызывать многократно).</summary>
        public static void Apply(FrameworkElement root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);
            SolidColorBrush separator = WfThemeStyles.Brush(palette.Separator);
            _ = WfThemeStyles.Brush(palette.SeparatorStrong);

            // 1) Ключи: в корень документа (ExpressionTextBox, ErrorView) и в сам DesignerView —
            //    его собственные ColorResources иначе перекрывают словарь приложения.
            ApplyToApplication(root.Resources, palette);
            WfActivityTheme.UseNativeControlStyles(root.Resources, palette);

            FrameworkElement designerView = FindByName(root, DesignerViewElementName);
            if (designerView != null)
            {
                ApplyToApplication(designerView.Resources, palette);
                SetBackground(designerView, surface);
                WfActivityTheme.UseNativeControlStyles(designerView.Resources, palette);
            }

            // 2) Поверхность: один фон на всю площадь.
            SetBackground(root, surface);

            // 3) Дефолтные цвета WF, пришедшие через StaticResource (не пересчитываются при смене темы).
            //    В режиме кэша (WfColorResourcesOverride) их место заняли связанные кисти палитры на этапе
            //    шаблона, поэтому подменять «запечённые» значения по значению больше не нужно.
            // 3) Режим кэша: цвета элемента активности приходят из наших связанных кистей на этапе
            //    шаблона, поэтому подменять «запечённые» дефолты WF по значению больше не нужно.
            if (!WfColorResourcesOverride.IsInstalled)
            {
                ReplaceWorkflowDefaultColors(root, palette);
            }

            // Активности: словари цветов WF, плита/рамка/подпись и нативные стили контролов внутри.
            WfActivityTheme.Apply(root, palette);

            // 4) Именованные части дизайнера.
            StatusBar shellBar = FindByName(root, ShellBarElementName) as StatusBar;
            if (shellBar != null)
            {
                ThemeShellBar(shellBar, palette);
            }

            // Хлебные крошки: элементы WF строит собственным DataTemplate, поэтому одного
            // Background у ListBox недостаточно — нужен отдельный проход (ThemeBreadCrumb).
            if (FindByName(root, BreadCrumbElementName) is ListBox breadCrumb)
            {
                ThemeBreadCrumb(breadCrumb, palette);
            }

            foreach (string name in new[] { MiniMapElementName, ExtensionSurfaceElementName })
            {
                if (FindByName(root, name) is Control panel)
                {
                    panel.Background = surface;
                    panel.BorderBrush = name == MiniMapElementName ? separator : Brushes.Transparent;
                    panel.Foreground = foreground;
                }
            }

            // 4б) Кнопки «Развернуть все»/«Свернуть все» в шапке дизайнера: подписи в них не влезали.
            ThemeHeaderButtons(root);

            // 5) Текст (подписи активностей, breadcrumb, watermark) и поля ввода.
            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(root))
            {
                // Привязку не разрываем: подпись активности может красить конвертер модели.
                if (!System.Windows.Data.BindingOperations.IsDataBound(textBlock, TextBlock.ForegroundProperty))
                {
                    textBlock.Foreground = foreground;
                }
            }

            // Поля ввода и скроллбары дизайнера — общий темизатор WF-контролов.
            WfVisualTreeThemer.Apply(root, palette);

            // Вкладки shell bar: подпись вкладки — TextBlock, и общий проход по тексту выше ставит ей
            // Foreground локальным значением. Состояние вкладки (активная/неактивная) переписывается
            // после него, иначе неактивные подписи светлели бы до цвета основного текста.
            if (shellBar != null)
            {
                ThemeShellBarItems(shellBar, palette);
            }

            // 5б) Таблицы панелей «Переменные», «Аргументы», «Пространства имён» (F-04): шапка, строки,
            //     ячейки и панели-контейнеры. Проход идёт после общего темизатора WF: иначе он вернул бы
            //     светлые кисти шаблона Aero2 поверх палитры, а строки виртуализованы.
            WfDataGridTheme.Apply(root, palette);

            // 5в) Поверхность дизайнера: полоса вокруг активностей. Идёт последней, потому что её красят
            //     несколько подпроходов с разными признаками «своё/чужое» — и после light → dark ни один
            //     из них не узнавал белый (см. RepaintSurfaceBand).
            RepaintSurfaceBand(root, palette);

            // 6) Адаптер ошибки: единственное место, где допустим красный.
            ControlTemplate errorTemplate = CreateValidationErrorTemplate(palette);
            foreach (Control control in FindVisualChildren<Control>(root))
            {
                Validation.SetErrorTemplate(control, errorTemplate);
            }
        }

        /// <summary>
        /// Пары «ключ ресурса → кисть» для шаблонов WF. Ключи, которые объявляет
        /// <c>ColorResources.xaml</c> и инспектор свойств, берутся из публичных
        /// <see cref="WorkflowDesignerColors"/> (строки в шаблонах — те же).
        /// </summary>
        internal static Dictionary<string, object> CreateResourceValues(StudioThemeColors palette)
        {
            StudioThemeColors colors = palette ?? StudioPalette.Dark;
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

            // F-05: ключи, на которые ссылаются шаблоны WF и RoslynExpressionEditor.xaml.
            Add(values, DesignerViewBackgroundKey, colors.Surface);
            Add(values, DesignerViewForegroundKey, colors.Foreground);

            // Поверхность дизайнера и элементы активности.
            Add(values, "WorkflowViewElementBackground", colors.Surface);
            Add(values, "WorkflowViewElementBorderBrush", colors.SeparatorStrong);
            Add(values, "WorkflowViewElementSelectedBackground", colors.Selected);
            Add(values, "WorkflowViewElementSelectedBorder", colors.SeparatorStrong);
            Add(values, "WorkflowViewElementCaptionBrush", colors.Foreground);
            Add(values, "WorkflowViewElementSelectedCaptionBrush", colors.Foreground);
            Add(values, "DesignerViewStatusBarBackground", colors.SurfaceAlt);
            Add(values, "ParallelSpacerColor", colors.Separator);
            Add(values, "FlowchartConnectorColor", colors.SeparatorStrong);

            // Shell bar: Imports / Variables / Arguments / Zoom / PanMode.
            Add(values, "ShellBarBackgroundBrush", colors.Surface);
            Add(values, "ShellBarControlBackground", colors.Surface);
            Add(values, "ShellBarForegroundActiveColor", colors.Foreground);
            Add(values, "ShellBarForegroundInactiveColor", colors.ForegroundMuted);
            Add(values, "ShellStatusBarSelectedBrush", colors.Selected);
            Add(values, "ShellStatusBarPressedBrush", colors.Selected);
            Add(values, "ShellStatusBarHoverBrush", colors.Hover);

            // Контекстные меню дизайнера (F-06).
            Add(values, "ContextMenuBackgroundBrush", colors.Surface);
            Add(values, "ContextMenuBorderBrush", colors.SeparatorStrong);
            Add(values, "ContextMenuIconAreaBrush", colors.Surface);
            Add(values, "ContextMenuTextBrush", colors.Foreground);
            Add(values, "ContextMenuTextHoverBrush", colors.Foreground);
            Add(values, "ContextMenuTextSelectedBrush", colors.Foreground);
            Add(values, "ContextMenuTextDisabledBrush", colors.ForegroundMuted);
            Add(values, "ContextMenuItemHoverBorderBrush", colors.SeparatorStrong);
            Add(values, "ContextMenuSeparatorBrush", colors.Separator);

            // Инспектор свойств: ключи отдаёт сам WF (публичные свойства WorkflowDesignerColors).
            Add(values, WorkflowDesignerColors.PropertyInspectorTextBrushKey, colors.Foreground);
            Add(values, WorkflowDesignerColors.PropertyInspectorSelectedForegroundBrushKey, colors.Foreground);
            Add(values, WorkflowDesignerColors.PropertyInspectorSelectedBackgroundBrushKey, colors.Selected);
            Add(values, WorkflowDesignerColors.PropertyInspectorBackgroundBrushKey, colors.Surface);
            Add(values, WorkflowDesignerColors.PropertyInspectorBorderBrushKey, colors.Separator);
            Add(values, WorkflowDesignerColors.PropertyInspectorCategoryCaptionTextBrushKey, colors.ForegroundMuted);
            Add(values, WorkflowDesignerColors.PropertyInspectorPaneBrushKey, colors.SurfaceAlt);
            Add(values, WorkflowDesignerColors.PropertyInspectorPopupBrushKey, colors.Surface);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarItemHoverBackgroundBrushKey, colors.Hover);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarItemHoverBorderBrushKey, colors.SeparatorStrong);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarItemSelectedBackgroundBrushKey, colors.Selected);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarItemSelectedBorderBrushKey, colors.SeparatorStrong);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarBackgroundBrushKey, colors.Surface);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarSeparatorBrushKey, colors.Separator);
            Add(values, WorkflowDesignerColors.PropertyInspectorToolBarTextBoxBorderBrushKey, colors.SeparatorStrong);

            // Шрифт WF-контролов (этап 12): WF берёт его из этих ключей, иначе панель «Свойства»,
            // сетка свойств и редакторы значений остаются на Segoe UI 12 (шрифт не наследуется от окна).
            values[WorkflowDesignerColors.FontFamilyKey] = StudioFonts.UiFont;
            values[WorkflowDesignerColors.FontSizeKey] = WfThemeStyles.FontSize;
            values[WorkflowDesignerColors.FontWeightKey] = FontWeights.Normal;

            return values;
        }

        /// <summary>
        /// Адаптер ошибки: hairline красного цвета палитры вокруг проблемного контрола.
        /// Красный — системная семантика ошибки, а не акцент (правило §1.2 плана).
        /// </summary>
        internal static ControlTemplate CreateValidationErrorTemplate(StudioThemeColors palette)
        {
            SolidColorBrush error = ErrorBrush(palette);
            // Кисть ошибки — единственный не-нейтральный цвет палитры (системная семантика).

            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));
            FrameworkElementFactory placeholder = new FrameworkElementFactory(typeof(AdornedElementPlaceholder));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Rectangle));
            border.SetValue(Shape.StrokeProperty, error);
            border.SetValue(Shape.StrokeThicknessProperty, 1d);
            border.SetValue(Shape.FillProperty, Brushes.Transparent);
            border.SetValue(Rectangle.RadiusXProperty, StudioPalette.TabCornerRadius);
            border.SetValue(Rectangle.RadiusYProperty, StudioPalette.TabCornerRadius);
            border.SetValue(UIElement.IsHitTestVisibleProperty, false);

            root.AppendChild(placeholder);
            root.AppendChild(border);

            // Значок ошибки в правом верхнем углу: одна рамка — это дефект «красная рамка без иконки».
            FrameworkElementFactory badge = new FrameworkElementFactory(typeof(Grid));
            badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            badge.SetValue(FrameworkElement.WidthProperty, 12d);
            badge.SetValue(FrameworkElement.HeightProperty, 12d);
            badge.SetValue(UIElement.IsHitTestVisibleProperty, false);

            FrameworkElementFactory circle = new FrameworkElementFactory(typeof(Ellipse));
            circle.SetValue(Shape.FillProperty, error);

            FrameworkElementFactory mark = new FrameworkElementFactory(typeof(TextBlock));
            mark.SetValue(TextBlock.TextProperty, "!");
            mark.SetValue(TextBlock.FontSizeProperty, 9d);
            mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            mark.SetValue(TextBlock.ForegroundProperty, WfThemeStyles.Brush((palette ?? StudioPalette.Dark).Surface));
            mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            badge.AppendChild(circle);
            badge.AppendChild(mark);
            root.AppendChild(badge);

            ControlTemplate template = new ControlTemplate(typeof(Control)) { VisualTree = root };
            template.Seal();
            return template;
        }

        /// <summary>Кисть ошибки: единственный не-нейтральный цвет палитры, зарезервированный под ошибки.</summary>
        internal static SolidColorBrush ErrorBrush(StudioThemeColors palette)
        {
            return WfThemeStyles.Brush((palette ?? StudioPalette.Dark).CloseHoverBackground);
        }

        /// <summary>
        /// Контекстное меню редактирования (Вырезать/Копировать/Вставить) на палитре. Единственная фабрика
        /// меню для <c>DesignerVariableContextMenuService</c>: принципы и стражи —
        /// docs\Designer_Variable_ContextMenu_Principles.md.
        /// </summary>
        internal static ContextMenu CreateEditingContextMenu(StudioThemeColors palette)
        {
            ContextMenu menu = new ContextMenu
            {
                // Закрытием управляет DesignerVariableContextMenuService (пункт меню, клик вне, Escape):
                // StaysOpen снимает автоматическое закрытие, HasDropShadow=false убирает «светлый»
                // прямоугольник тени поверх тёмной темы.
                StaysOpen = true,
                HasDropShadow = false,
                BorderThickness = new Thickness(1)
            };

            _ = menu.Items.Add(CreateMenuItem("Вырезать", ApplicationCommands.Cut));
            _ = menu.Items.Add(CreateMenuItem("Копировать", ApplicationCommands.Copy));
            _ = menu.Items.Add(CreateMenuItem("Вставить", ApplicationCommands.Paste));

            RecolorEditingContextMenu(menu, palette);

            return menu;
        }

        /// <summary>
        /// Перекраска уже созданного меню под текущую палитру: меню создаётся один раз на поле ввода,
        /// а тема может меняться, поэтому цвета применяются на каждое открытие. Структура, заголовки и
        /// команды не меняются — перекрашивать можно сколько угодно раз.
        /// </summary>
        internal static void RecolorEditingContextMenu(ContextMenu menu, StudioThemeColors palette)
        {
            if (menu == null)
            {
                return;
            }

            // Кисти, шаблон меню, панель пунктов и стиль контейнера пункта — в одной точке
            // (WfContextMenuTheme): локальных Background/Foreground на чужом шаблоне недостаточно.
            // Меню живёт в popup и в проходы темы по дереву дизайнера не попадает, а подсветку, углы и
            // тень рисует чужой шаблон (MaterialDesign/Aero2), причём подсветка строится от Foreground —
            // в тёмной теме это светлая накладка под белыми буквами.
            WfContextMenuTheme.Apply(menu, palette);
        }

        private static MenuItem CreateMenuItem(string header, ICommand command)
        {
            return new MenuItem
            {
                Header = header,
                Command = command
            };
        }

        private static void Add(Dictionary<string, object> values, string key, Color color)
        {
            if (!string.IsNullOrEmpty(key))
            {
                values[key] = WfThemeStyles.Brush(color);
            }
        }

        private static ResourceDictionary GetOrCreatePaletteDictionary(ResourceDictionary target)
        {
            for (int index = 0; index < target.MergedDictionaries.Count; index++)
            {
                ResourceDictionary merged = target.MergedDictionaries[index];
                if (merged != null && merged.Contains(PaletteDictionaryMarker))
                {
                    // Словарь палитры всегда последний: MergedDictionaries просматриваются с конца,
                    // а WF добавляет свои FontAndColorResources в Application.Resources при создании
                    // каждого WorkflowDesigner — иначе дефолты WF перекрывают палитру.
                    _ = target.MergedDictionaries.Remove(merged);
                    target.MergedDictionaries.Add(merged);
                    return merged;
                }
            }

            ResourceDictionary theme = new ResourceDictionary { [PaletteDictionaryMarker] = PaletteDictionaryMarker };
            target.MergedDictionaries.Add(theme);
            return theme;
        }

        /// <summary>
        /// Заменяет дефолтные цвета WF, пришедшие в элементы через <c>{StaticResource}</c>
        /// (белая поверхность дизайнера, фон/рамка/подпись элемента активности). Пользовательские
        /// кисти не трогаются: сравнивается точное значение дефолта.
        /// <para>
        /// Проход двусторонний: «дефолт WF → цвет палитры» <b>и</b> «цвет противоположной палитры → тот же
        /// токен активной» (<see cref="WithStalePaletteColour"/>). Без второго правила возврат
        /// «тёмная → светлая» оставлял бы тёмные плашки: дефолт уже подменён, а тёмный цвет палитры в
        /// списке дефолтов отсутствует, поэтому замена не срабатывала никогда.
        /// </para>
        /// <para>
        /// Регресс: пустая активность в дизайнере (плита <c>Border</c> 440×440 вокруг подсказки
        /// «Перетащите сюда действие») после light → dark → light оставалась цвета <c>Surface</c> тёмной
        /// палитры — на светлой теме была видна чёрная рамка толщиной с бордер плиты.
        /// </para>
        /// </summary>
        private static void ReplaceWorkflowDefaultColors(DependencyObject root, StudioThemeColors palette)
        {
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush separatorStrong = WfThemeStyles.Brush(palette.SeparatorStrong);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);

            Color[] designerBackgrounds = WithStalePaletteColour(
                new[]
                {
                    SafeColor(() => WorkflowDesignerColors.DesignerViewBackgroundColor, FallbackDesignerViewBackground),
                    FallbackDesignerViewBackground
                },
                palette,
                StudioPalette.SurfaceToken);
            Color[] elementBackgrounds = WithStalePaletteColour(
                new[]
                {
                    SafeColor(() => WorkflowDesignerColors.WorkflowViewElementBackgroundColor, FallbackViewElementBackground),
                    FallbackViewElementBackground
                },
                palette,
                StudioPalette.SurfaceToken);
            Color[] elementBorders = WithStalePaletteColour(
                new[]
                {
                    SafeColor(() => WorkflowDesignerColors.WorkflowViewElementBorderColor, FallbackViewElementBorder),
                    FallbackViewElementBorder
                },
                palette,
                StudioPalette.SeparatorStrongToken);
            Color[] captions = WithStalePaletteColour(
                new[]
                {
                    SafeColor(() => WorkflowDesignerColors.WorkflowViewElementCaptionColor, FallbackViewElementCaption),
                    FallbackViewElementCaption
                },
                palette,
                StudioPalette.ForegroundToken);

            foreach (DependencyObject element in SelfAndDescendants(root))
            {
                ReplaceColor(element, Panel.BackgroundProperty, designerBackgrounds, surface);
                ReplaceColor(element, Panel.BackgroundProperty, elementBackgrounds, surface);
                ReplaceColor(element, Control.BackgroundProperty, designerBackgrounds, surface);
                ReplaceColor(element, Control.BackgroundProperty, elementBackgrounds, surface);
                ReplaceColor(element, Border.BackgroundProperty, designerBackgrounds, surface);
                ReplaceColor(element, Border.BackgroundProperty, elementBackgrounds, surface);
                ReplaceColor(element, Border.BorderBrushProperty, elementBorders, separatorStrong);
                ReplaceColor(element, Control.BorderBrushProperty, elementBorders, separatorStrong);
                ReplaceColor(element, Control.ForegroundProperty, captions, foreground);
                ReplaceColor(element, TextBlock.ForegroundProperty, captions, foreground);
            }
        }

        private static void ReplaceColor(DependencyObject element, DependencyProperty property, Color[] from, Brush to)
        {
            if (element == null || property == null || to == null)
            {
                return;
            }

            // Только локально заданные кисти: значения из шаблонов и привязок не переписываем.
            if (!(element.ReadLocalValue(property) is SolidColorBrush current))
            {
                return;
            }

            foreach (Color color in from)
            {
                if (current.Color == color)
                {
                    element.SetValue(property, to);
                    return;
                }
            }
        }

        /// <summary>
        /// Дополняет список «заменяемых» цветов цветом <b>того же токена противоположной палитры</b>:
        /// так проход узнаёт собственную работу предыдущей темы и переносит её в активную.
        /// <para>
        /// Токен задаётся именем, а не значением, поэтому пары совпадают для любой палитры: фон плиты,
        /// покрашенный в <c>Surface</c>, узнаётся как <c>Surface</c> и становится <c>Surface</c> активной
        /// темы, рамка — <c>SeparatorStrong</c>, подпись — <c>Foreground</c>. Полупрозрачные токены
        /// (<c>Overlay*</c>) пропускаются: локальная плашка ими не красится.
        /// </para>
        /// </summary>
        private static Color[] WithStalePaletteColour(Color[] defaults, StudioThemeColors palette, string tokenName)
        {
            if (defaults == null || palette == null || string.IsNullOrEmpty(tokenName))
            {
                return defaults;
            }

            StudioThemeColors previous = StudioPalette.For(palette.Theme.Other());
            if (!previous.Tokens.TryGetValue(tokenName, out Color stale)
                || stale.A != 0xFF
                || Array.IndexOf(defaults, stale) >= 0)
            {
                return defaults;
            }

            Color[] extended = new Color[defaults.Length + 1];
            Array.Copy(defaults, extended, defaults.Length);
            extended[defaults.Length] = stale;
            return extended;
        }

        private static Color SafeColor(Func<Color> getter, Color fallback)
        {
            try
            {
                Color color = getter();
                return color.A == 0 ? fallback : color;
            }
            catch (Exception)
            {
                // Сборка WF может быть не инициализирована — тогда остаётся дефолт из Reference Source.
                return fallback;
            }
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

        /// <summary>
        /// Возвращает поверхность дизайнера в палитру активной темы — «белая полоса вокруг активностей»
        /// после перехода light → dark.
        /// <para>
        /// Почему баг был односторонним. Полосу красят разные проходы (<see cref="WfDataGridTheme"/> —
        /// хост таблиц, <see cref="WfVisualTreeThemer"/> — корень документа, <see cref="WfActivityTheme"/> —
        /// плиты), и каждый узнаёт «своё» значение по своему признаку. В сторону светлой темы все они
        /// срабатывают (тёмная плашка палитры — не «светлая»), а в сторону тёмной белый не узнавался:
        /// <see cref="WfThemeStyles.TryMapStalePaletteColour"/> отбрасывал цвета, входящие в активную
        /// палитру, а в тёмной палитре чистый белый объявлен как <c>CloseHoverForeground</c>. В результате
        /// активности перекрашивались (у них свои связанные кисти), а полоса вокруг оставалась белой.
        /// </para>
        /// <para>
        /// Признак прохода — «это цвет противоположной палитры», а не «это не наш цвет»: пользовательские
        /// кисти (не токены палитры) остаются нетронутыми. Проход идемпотентен: его собственная работа
        /// (поверхность активной темы) уже входит в токены и не переписывается. Внутрь
        /// <see cref="WorkflowViewElement"/> и в <see cref="System.Windows.Documents.AdornerLayer"/> не
        /// заходим: там живые связанные кисти <see cref="WfColorResourcesOverride"/> и адаптеры ошибок, и
        /// локальное значение сломало бы и то, и другое.
        /// </para>
        /// </summary>
        private static void RepaintSurfaceBand(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);

            foreach (DependencyObject element in SurfaceBandElements(root))
            {
                DependencyProperty property;
                Brush current;

                if (element is Panel panel)
                {
                    property = Panel.BackgroundProperty;
                    current = panel.Background;
                }
                else if (element is Border border)
                {
                    property = Border.BackgroundProperty;
                    current = border.Background;
                }
                else if (element is Control control)
                {
                    property = Control.BackgroundProperty;
                    current = control.Background;
                }
                else
                {
                    continue;
                }

                // Привязку не разрываем: фон может приходить из модели активности.
                if (System.Windows.Data.BindingOperations.IsDataBound(element, property))
                {
                    continue;
                }

                // Меняем только собственную работу предыдущей темы: цвет её палитры. Прозрачность,
                // градиенты, цвета активной темы и пользовательские кисти (не токены палитры) не трогаем.
                if (!WfThemeStyles.TryMapStalePaletteColour(current, palette, out _))
                {
                    continue;
                }

                element.SetValue(property, surface);
            }
        }

        /// <summary>
        /// Элементы поверхности дизайнера: поддерево <paramref name="root"/> без содержимого активностей
        /// (у активностей своя темизация и связанные кисти) и без слоя адорнеров (адаптеры ошибок).
        /// </summary>
        private static IEnumerable<DependencyObject> SurfaceBandElements(DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            var pending = new Stack<DependencyObject>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                DependencyObject element = pending.Pop();

                if (element is WorkflowViewElement || element is System.Windows.Documents.AdornerLayer)
                {
                    continue;
                }

                yield return element;

                foreach (DependencyObject child in EnumerateChildren(element))
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Поиск элемента по <c>Name</c> в логическом и визуальном дереве: логические дети существуют
        /// ещё до применения шаблона, визуальные — после. Используется и при загрузке активности.
        /// </summary>
        internal static FrameworkElement FindByName(DependencyObject root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (root is FrameworkElement self && self.Name == name)
            {
                return self;
            }

            foreach (FrameworkElement element in FindVisualChildren<FrameworkElement>(root))
            {
                if (element.Name == name)
                {
                    return element;
                }
            }

            return null;
        }

        private static IEnumerable<DependencyObject> SelfAndDescendants(DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            yield return root;

            foreach (DependencyObject descendant in FindVisualChildren<DependencyObject>(root))
            {
                yield return descendant;
            }
        }

        /// <summary>
        /// Дети элемента: логические (<see cref="LogicalTreeHelper"/>) — они существуют ещё до применения
        /// шаблона (<c>UserControl.Content</c> дизайнера), и визуальные (<see cref="VisualTreeHelper"/>) —
        /// они появляются после. Дубликаты отсекаются, поэтому обход не разрастается.
        /// </summary>
        private static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject parent)
        {
            if (parent == null)
            {
                yield break;
            }

            HashSet<DependencyObject> seen = new HashSet<DependencyObject>();

            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is DependencyObject logicalChild && seen.Add(logicalChild))
                {
                    yield return logicalChild;
                }
            }

            if (parent is Visual)
            {
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    DependencyObject visualChild = VisualTreeHelper.GetChild(parent, i);
                    if (seen.Add(visualChild))
                    {
                        yield return visualChild;
                    }
                }
            }
        }


        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }

            foreach (DependencyObject child in EnumerateChildren(parent))
            {
                // Дети уже перечислены EnumerateChildren: логические (до применения шаблона) + визуальные.

                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }
        /// <summary>
        /// Кнопки «Развернуть все»/«Свернуть все» в шапке <c>DesignerView</c>.
        /// <para>
        /// Регресс: кнопки WF (две <see cref="ToggleButton"/>, шаблон <c>ExpandAllCollapseAllTemplate</c>)
        /// получают неявный стиль <c>ToggleButton</c> MaterialDesign из <c>App.xaml</c>, а там
        /// <c>Width = 34</c>. Подпись «Развернуть все» ≈83 px в такую кнопку не влезает: она вылезала за
        /// кнопку и обрезалась правой кромкой панели — в шапке читались только «разв» и «свер».
        /// </para>
        /// <para>
        /// Лечение: локальные <see cref="FrameworkElement.Width"/> = <c>NaN</c> и
        /// <see cref="FrameworkElement.MaxWidth"/> = бесконечность перебивают стиль, а
        /// <see cref="FrameworkElement.MinWidth"/> считается по фактической ширине подписи
        /// (<see cref="FormattedText"/>, тем же шрифтом и размером) плюс отступы подписи, паддинг и рамка
        /// кнопки. Пара кнопок дополнительно отступает от правой кромки на
        /// <see cref="HeaderButtonsRightInset"/>: отступ ставится последней кнопке панели, поэтому
        /// сдвигается вся пара, а не растёт зазор между кнопками. Проход идемпотентен — <c>Math.Max</c>
        /// по <c>MinWidth</c> и <c>Margin.Right</c> не накапливает значения при смене темы.
        /// </para>
        /// </summary>
        internal static void ThemeHeaderButtons(FrameworkElement root)
        {
            ButtonBase expand = FindByName(root, ExpandAllButtonName) as ButtonBase;
            ButtonBase collapse = FindByName(root, CollapseAllButtonName) as ButtonBase;

            foreach (ButtonBase button in new[] { expand, collapse })
            {
                if (button == null)
                {
                    continue;
                }

                double captionWidth = CaptionWidthOf(button);
                if (captionWidth <= 0d)
                {
                    continue;
                }

                // Локальные значения важнее сеттеров неявного стиля MaterialDesign (Width = 34).
                button.Width = double.NaN;
                button.MaxWidth = double.PositiveInfinity;

                // 2 px — запас на округление раскладки (WPF считает в device-independent единицах).
                double minWidth = Math.Ceiling(captionWidth + CaptionInsetsOf(button) + 2d);
                if (button.MinWidth < minWidth)
                {
                    button.MinWidth = minWidth;
                }
            }

            ButtonBase last = LastInPair(expand, collapse);
            if (last == null)
            {
                return;
            }

            Thickness margin = last.Margin;
            if (margin.Right < HeaderButtonsRightInset)
            {
                last.Margin = new Thickness(margin.Left, margin.Top, HeaderButtonsRightInset, margin.Bottom);
            }
        }

        /// <summary>Подпись кнопки шапки: часть шаблона WF, а если шаблон ещё не применён — первый TextBlock.</summary>
        private static TextBlock CaptionOf(ButtonBase button)
        {
            button.ApplyTemplate();

            if (button.Template != null
                && button.Template.FindName(HeaderCaptionPartName, button) is TextBlock caption)
            {
                return caption;
            }

            return FindVisualChildren<TextBlock>(button).FirstOrDefault();
        }

        private static double CaptionWidthOf(ButtonBase button)
        {
            TextBlock caption = CaptionOf(button);
            if (caption == null || string.IsNullOrEmpty(caption.Text))
            {
                return 0d;
            }

            try
            {
                var formatted = new FormattedText(
                    caption.Text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch),
                    caption.FontSize,
                    Brushes.Black,
                    VisualTreeHelper.GetDpi(button).PixelsPerDip);

                return formatted.WidthIncludingTrailingWhitespace;
            }
            catch (Exception)
            {
                // Экзотический шрифт/размер: остаётся раскладка WF, но кнопка всё равно растянется
                // по содержимому (Width снят выше).
                return 0d;
            }
        }

        /// <summary>Отступы вокруг подписи: Margin подписи в шаблоне WF + паддинг и рамка кнопки.</summary>
        private static double CaptionInsetsOf(ButtonBase button)
        {
            TextBlock caption = CaptionOf(button);
            if (caption == null)
            {
                return 0d;
            }

            return caption.Margin.Left + caption.Margin.Right
                   + button.Padding.Left + button.Padding.Right
                   + button.BorderThickness.Left + button.BorderThickness.Right;
        }

        /// <summary>
        /// Правая кнопка пары (в шаблоне WF это <c>collapseAllButton</c>): отступ ставится ей, чтобы
        /// сдвинулась вся пара.
        /// </summary>
        private static ButtonBase LastInPair(ButtonBase first, ButtonBase second)
        {
            if (first == null)
            {
                return second;
            }

            if (second == null)
            {
                return first;
            }

            if (first.Parent is Panel panel && ReferenceEquals(first.Parent, second.Parent))
            {
                int firstIndex = panel.Children.IndexOf(first);
                int secondIndex = panel.Children.IndexOf(second);
                return secondIndex >= firstIndex ? second : first;
            }



            // Кнопки живут в одной панели шапки; если родители разные, правая — «Свернуть все».
            return second;
        }



        // --- Хлебные крошки (Main › Sequence › Assign): имена активностей на палитре. ---


        /// <summary>Подписки на генератор элементов крошек: палитра обновляется каждым применением темы.</summary>
        private static readonly ConditionalWeakTable<ItemsControl, BreadCrumbHook> BreadCrumbHooks =
            new ConditionalWeakTable<ItemsControl, BreadCrumbHook>();

        /// <summary>
        /// Оформление shell bar дизайнера: один фон с поверхностью, hairline вместо акцентной полосы.
        /// Отдельный метод, потому что shell bar появляется в дереве раньше темизации поверхности —
        /// его оформляет <c>LoadActivityToDesignerHelper.SetVariablesStyle</c> при загрузке активности.
        /// </summary>
        internal static void ThemeShellBar(StatusBar shellBar, StudioThemeColors palette)
        {
            if (shellBar == null || palette == null)
            {
                return;
            }

            shellBar.Background = WfThemeStyles.Brush(palette.Surface);
            shellBar.BorderBrush = WfThemeStyles.Brush(palette.Separator);
            shellBar.BorderThickness = new Thickness(0, 1, 0, 0);
            shellBar.Foreground = WfThemeStyles.Brush(palette.Foreground);
            shellBar.FontFamily = WfThemeStyles.Font;

            // Вкладки «Переменные / Аргументы / Пространства имён», PanMode, масштаб: у кнопок системной
            // темы светлые плашки и рамка фокуса, а часть кистей WF берёт через StaticResource.
            ThemeShellBarItems(shellBar, palette);

            // Масштаб (zoomPicker) — editable ComboBox: Foreground он наследует от shell bar, а плашку под
            // текстом рисует шаблон Aero2 (белая). Без этого прохода свёрнутый список читается как белые
            // буквы на белом фоне (регресс F-04): путь Apply у shell bar полного обхода не делает.
            WfVisualTreeThemer.ThemeComboBoxes(shellBar, palette);
        }

        /// <summary>
        /// Кнопки-вкладки shell bar («Переменные», «Аргументы», «Пространства имён», PanMode, масштаб).
        /// <para>
        /// Стиль WF <c>statusBarButtonStyle</c> тянет цвета через <c>DynamicResource</c>, но этого мало:
        /// 1) <c>ShellStatusBarHoverBrush</c> в WF — светлый градиент Aero2, а плашку и рамку фокуса рисует
        /// шаблон системной темы (его триггеры сильнее сеттеров стиля); 2) часть кистей приходит
        /// <c>StaticResource</c> и после смены темы не пересчитывается. Поэтому состояния ставятся
        /// локальными значениями: активная вкладка — <c>Selected</c>, hover — <c>Hover</c>, неактивная
        /// подпись — <c>ForegroundMuted</c>, активная — <c>Foreground</c>.
        /// </para>
        /// </summary>
        internal static void ThemeShellBarItems(StatusBar shellBar, StudioThemeColors palette)
        {
            if (shellBar == null || palette == null)
            {
                return;
            }

            foreach (StatusBarItem item in FindVisualChildren<StatusBarItem>(shellBar))
            {
                item.Background = Brushes.Transparent;
                item.BorderBrush = Brushes.Transparent;
                item.Foreground = WfThemeStyles.Brush(palette.Foreground);
                item.FontFamily = WfThemeStyles.Font;
            }

            foreach (ButtonBase button in FindVisualChildren<ButtonBase>(shellBar))
            {
                // Кнопки раскрывающегося списка масштаба и полос прокрутки — не вкладки shell bar:
                // у них собственные проходы (ThemeComboBoxes, WfVisualTreeThemer).
                if (IsInsideDropDownChrome(button))
                {
                    continue;
                }

                ThemeShellBarButton(button, palette);
            }
        }

        private static bool IsInsideDropDownChrome(DependencyObject element)
        {
            return XamlHelper.FindVisualParent<ComboBox>(element) != null
                || XamlHelper.FindVisualParent<ScrollBar>(element) != null
                // Кнопка раскрытия комбобокса ещё не имеет визуального предка-комбобокса до применения
                // шаблона: её владелец — TemplatedParent.
                || (element is FrameworkElement frameworkElement && frameworkElement.TemplatedParent is ComboBox);
        }

        private static void ThemeShellBarButton(ButtonBase button, StudioThemeColors palette)
        {
            // IsChecked есть только у ToggleButton: подписка на чужое свойство недопустима.
            DependencyProperty[] states = button is ToggleButton
                ? new[]
                {
                    UIElement.IsMouseOverProperty,
                    ButtonBase.IsPressedProperty,
                    UIElement.IsKeyboardFocusedProperty,
                    ToggleButton.IsCheckedProperty
                }
                : new[]
                {
                    UIElement.IsMouseOverProperty,
                    ButtonBase.IsPressedProperty,
                    UIElement.IsKeyboardFocusedProperty
                };

            WfThemeStyles.WatchState(button, palette, (element, colors) =>
            {
                if (!(element is ButtonBase target))
                {
                    return;
                }

                bool active = (target is ToggleButton toggle && toggle.IsChecked == true)
                    || target.IsPressed
                    || target.IsKeyboardFocused;

                target.Foreground = WfThemeStyles.Brush(active || target.IsMouseOver ? colors.Foreground : colors.ForegroundMuted);
                target.Background = active
                    ? WfThemeStyles.Brush(colors.Selected)
                    : target.IsMouseOver ? WfThemeStyles.Brush(colors.Hover) : Brushes.Transparent;
                target.BorderBrush = Brushes.Transparent;
                target.BorderThickness = new Thickness(0);
                target.FontFamily = WfThemeStyles.Font;

                // Подпись вкладки — TextBlock внутри кнопки. Общий проход по тексту ставит ей Foreground
                // локальным значением, поэтому состояние вкладки переписывается и здесь.
                foreach (TextBlock text in FindVisualChildren<TextBlock>(target))
                {
                    text.Foreground = target.Foreground;
                }
            }, states);
        }

        /// <summary>
        /// Хлебные крошки дизайнера. Регресс: к именам активностей тема не применялась — белые плашки
        /// поверх тёмной поверхности.
        /// <para>
        /// Крошка — это <see cref="ListBox"/>, элементы которого WF строит собственным DataTemplate:
        /// кнопка перехода с chrome Aero2 и <see cref="TextBlock"/> на системных кистях (<c>SystemColors</c>,
        /// <c>Colors.White</c>). Одного <c>ListBox.Background</c> недостаточно, поэтому:
        /// 1) контейнеры получают стиль палитры (<see cref="WfVisualTreeThemer.CreateListItemStyle"/>) —
        ///    он закрывает и те элементы, которые генератор создаст позже;
        /// 2) содержимое уже материализованных крошек перекрашивается;
        /// 3) крошки, появившиеся после темизации (пользователь «провалился» в <c>Sequence</c>/<c>Assign</c>),
        ///    достраиваются по <see cref="ItemContainerGenerator.StatusChanged"/>.
        /// </para>
        /// </summary>
        internal static void ThemeBreadCrumb(ListBox breadCrumb, StudioThemeColors palette)
        {
            if (breadCrumb == null || palette == null)
            {
                return;
            }

            breadCrumb.Background = WfThemeStyles.Brush(palette.Surface);
            breadCrumb.Foreground = WfThemeStyles.Brush(palette.Foreground);
            breadCrumb.BorderBrush = WfThemeStyles.Brush(palette.Separator);
            breadCrumb.BorderThickness = new Thickness(0);
            breadCrumb.FontFamily = WfThemeStyles.Font;

            // Артефакт шириной в пиксель вылезал за нижнюю границу полосы крошек и ложился на hairline
            // «крошки/дизайнер»: содержимое крошки не имеет права рисовать вне своей полосы, а дробные
            // координаты давали полупрозрачную полоску на стыке.
            breadCrumb.ClipToBounds = true;
            breadCrumb.UseLayoutRounding = true;
            breadCrumb.SnapsToDevicePixels = true;
            breadCrumb.Padding = new Thickness(0);

            // Селектор стилей элементов (как в ToolboxControl) сильнее ItemContainerStyle:
            // подменять стиль в этом случае запрещено (см. AGENTS.md, ToolboxDragContractTests).
            if (breadCrumb.ItemContainerStyleSelector == null)
            {
                breadCrumb.ItemContainerStyle = WfVisualTreeThemer.CreateListItemStyle(breadCrumb.ItemContainerStyle, palette);
            }

            RefreshBreadCrumb(breadCrumb, palette);
            HookBreadCrumbContainers(breadCrumb, palette);

            // Шаблон содержимого крошки и ItemsPanel приходят из шаблонов WF со {StaticResource} на белый
            // DesignerViewBackground и создаются уже после применения темы, поэтому проход повторяется
            // при изменении поддерева (крошки добавляются при провале в активность).
            HookBreadCrumbLayout(breadCrumb, palette);
        }

        /// <summary>
        /// Полный проход по крошке: сначала непрозрачная подложка полосы, затем содержимое и гашение
        /// светлых кистей. Порядок обязателен: кисть, погашенная в <c>Transparent</c>, показывает то,
        /// что лежит под полосой.
        /// </summary>
        private static void RefreshBreadCrumb(ListBox breadCrumb, StudioThemeColors palette)
        {
            PaintBreadCrumbBand(breadCrumb, palette);
            ThemeBreadCrumbContent(breadCrumb, palette);
        }

        /// <summary>
        /// Непрозрачная подложка полосы крошек.
        /// <para>
        /// Регресс: имена активностей стали белыми целиком на тёмной теме. Причина — гашение светлой
        /// кисти в <c>Transparent</c>: шаблон <c>ListBox</c>, его <c>ScrollViewer</c> и <c>ItemsPanel</c>
        /// WF объявляет с <c>{StaticResource}</c> на белый <c>DesignerViewBackground</c>
        /// (<c>StaticResource</c> не пересчитывается при смене темы), а хост полосы (Grid
        /// <c>BreadCrumbBarLayout</c> и обёртки до панели дизайнера) остаётся белым. Как только фон
        /// содержимого становится прозрачным, этот белый предок виден целиком — отсюда и «один пиксель»
        /// на стыке, и белая полоса. Поэтому подложка не гасится, а красится цветом <c>Surface</c>.
        /// </para>
        /// </summary>
        private static void PaintBreadCrumbBand(ListBox breadCrumb, StudioThemeColors palette)
        {
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);

            // Части шаблона (Border/ScrollViewer/ScrollContentPresenter/ItemsPanel) появляются после
            // загрузки документа, поэтому шаблон применяется явно.
            _ = breadCrumb.ApplyTemplate();

            foreach (DependencyObject element in SelfAndDescendants(breadCrumb))
            {
                // Элемент самой крошки — не полоса: его фон означает состояние (hover/selected)
                // и приходит триггерами стиля.
                if (IsInsideBreadCrumbItem(element))
                {
                    continue;
                }

                // Угловой Rectangle шаблона ScrollViewer (Aero2) вырождается в 1×1 и рисует светлую
                // точку на стыке с дизайнером — это артефакт темы, а не разделитель.
                if (element is Shape corner && IsTemplateCorner(corner))
                {
                    corner.Fill = surface;
                    corner.Stroke = surface;
                    continue;
                }

                SetOpaqueBackground(element, surface);
            }

            for (DependencyObject current = ParentOf(breadCrumb); current != null; current = ParentOf(current))
            {
                SetOpaqueBackground(current, surface);

                if (current is FrameworkElement host
                    && string.Equals(host.Name, DesignerViewElementName, StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        /// <summary>Красит фон непрозрачной кистью, если элемент вообще способен рисовать фон.</summary>
        private static void SetOpaqueBackground(DependencyObject element, Brush background)
        {
            switch (element)
            {
                case Panel panel:
                    panel.Background = background;
                    break;

                case Border border:
                    border.Background = background;
                    break;

                case Control control:
                    control.Background = background;
                    break;
                default:
                    break;
            }
        }

        /// <summary>Элемент принадлежит крошке: его фон — состояние, а не фон полосы.</summary>
        private static bool IsInsideBreadCrumbItem(DependencyObject element)
        {
            for (DependencyObject current = element; current != null; current = ParentOf(current))
            {
                if (current is ListBoxItem)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Родитель в визуальном дереве (для не-визуальных узлов — логический).</summary>
        private static DependencyObject ParentOf(DependencyObject element)
        {
            return element == null
                ? null
                : element is Visual
                ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }


        /// <summary>
        /// Перекрашивает содержимое уже материализованных крошек: контейнер прозрачный (состояния рисует
        /// триггер стиля), кнопка перехода — без chrome, текст и шевроны — из палитры.
        /// </summary>
        private static void ThemeBreadCrumbContent(ListBox breadCrumb, StudioThemeColors palette)
        {
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush muted = WfThemeStyles.Brush(palette.ForegroundMuted);

            foreach (ListBoxItem item in FindVisualChildren<ListBoxItem>(breadCrumb))
            {
                item.Background = Brushes.Transparent;
                item.BorderBrush = Brushes.Transparent;
                item.Foreground = foreground;
                item.FontFamily = WfThemeStyles.Font;
                item.FontSize = WfThemeStyles.FontSize;
            }

            // Шаблон кнопки крошки не подменяется: в нём навигация по крошкам и раскрытие списка
            // вложенных активностей — теряются обработчики WF. Гасятся только кисти.
            foreach (ButtonBase button in FindVisualChildren<ButtonBase>(breadCrumb))
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
                button.Foreground = foreground;
                button.FontFamily = WfThemeStyles.Font;
                button.FontSize = WfThemeStyles.FontSize;
            }

            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(breadCrumb))
            {
                // Привязку не разрываем: подпись активности может красить конвертер модели.
                if (!System.Windows.Data.BindingOperations.IsDataBound(textBlock, TextBlock.ForegroundProperty))
                {
                    textBlock.Foreground = foreground;
                }
                textBlock.FontFamily = WfThemeStyles.Font;
            }

            // Шевроны-разделители крошек: системный чёрный на тёмной поверхности не виден.
            foreach (Shape shape in FindVisualChildren<Shape>(breadCrumb))
            {
                // Угловой пиксель шаблона (1×1) не шеврон: он сливается с поверхностью полосы,
                // иначе справа внизу от имени последней активности остаётся светлая точка (F-04).
                if (IsTemplateCorner(shape))
                {
                    shape.Fill = surface;
                    shape.Stroke = surface;
                    continue;
                }

                shape.Fill = muted;
                shape.Stroke = muted;
            }

            NeutralizeBreadCrumbBrushes(breadCrumb, palette);
        }


        /// <summary>
        /// Угловой артефакт шаблона: <c>Rectangle</c> в правом нижнем углу <c>ScrollViewer</c> Aero2
        /// (светло-серый фон темы Aero2), который при скрытых полосах прокрутки вырождается в 1×1. Это и
        /// есть «один белый пиксель» на стыке крошек и дизайнера: элемент не рисует разделитель и не
        /// является шевроном, поэтому он обязан сливаться с поверхностью, а не получать чужой цвет.
        /// </summary>
        private static bool IsTemplateCorner(Shape shape)
        {
            if (!(shape is Rectangle))
            {
                return false;
            }

            double width = double.IsNaN(shape.Width) ? shape.ActualWidth : shape.Width;
            double height = double.IsNaN(shape.Height) ? shape.ActualHeight : shape.Height;

            return width <= 1d && height <= 1d;
        }


        /// <summary>
        /// Крошки создаются генератором элементов уже после применения темы, поэтому содержимое новых
        /// крошек достраивается по <see cref="ItemContainerGenerator.StatusChanged"/>. Подписка ставится
        /// один раз на <see cref="ListBox"/>: обработчик живёт на самом элементе, а повторное применение
        /// темы только обновляет палитру в подписке (снятие по <c>Unloaded</c> потеряло бы темизацию
        /// после переключения вкладки документа).
        /// </summary>
        private static void HookBreadCrumbContainers(ListBox breadCrumb, StudioThemeColors palette)
        {
            BreadCrumbHook hook = BreadCrumbHooks.GetValue(breadCrumb, key => new BreadCrumbHook());
            hook.Palette = palette;

            if (hook.Subscribed)
            {
                return;
            }

            hook.Subscribed = true;

            breadCrumb.ItemContainerGenerator.StatusChanged += (sender, args) =>
            {
                if (!(sender is ItemContainerGenerator generator) || generator.Status != GeneratorStatus.ContainersGenerated)
                {
                    return;
                }

                if (!BreadCrumbHooks.TryGetValue(breadCrumb, out BreadCrumbHook current))
                {
                    return;
                }

                // Шаблон содержимого достраивается после генерации контейнеров: проход повторяется
                // на следующем цикле диспетчера, иначе крошка ещё пуста.
                _ = breadCrumb.Dispatcher.BeginInvoke(
                    new Action(() => RefreshBreadCrumb(breadCrumb, current.Palette)),
                    DispatcherPriority.Background);
            };
        }

        /// <summary>
        /// Гасит светлые кисти внутри крошки. Реализация общая с тулбоксом —
        /// <see cref="WfThemeStyles.NeutralizeLightBrushes"/>: правила (что считать светлой плашкой и как
        /// пережить смену темы) обязаны быть одни на все WF-контролы, иначе регрессы возвращаются по одному.
        /// </summary>
        private static void NeutralizeBreadCrumbBrushes(ListBox breadCrumb, StudioThemeColors palette)
        {
            WfThemeStyles.NeutralizeLightBrushes(breadCrumb, palette);
        }


        /// <summary>
        /// Свойства типа <see cref="Brush"/> элемента, включая унаследованные. Реализация и кэш на тип
        /// живут в <see cref="WfThemeStyles.BrushPropertiesOf"/> — один кэш на всю темизацию WF.
        /// </summary>
        internal static IReadOnlyList<DependencyProperty> BrushPropertiesOf(Type type)
        {
            return WfThemeStyles.BrushPropertiesOf(type);
        }


        /// <summary>
        /// Повторный проход по раскладке: шаблон содержимого крошки строится после контейнеров, а
        /// <c>ItemsPanel</c> и <c>ScrollViewer</c> приходят из шаблонов WF с <c>{StaticResource}</c> на
        /// белый <c>DesignerViewBackground</c> — <c>StaticResource</c> не пересчитывается, поэтому
        /// «поздний» элемент оставался светлым даже после применения темы. Проход выполняется только при
        /// изменении отпечатка поддерева, иначе <see cref="FrameworkElement.LayoutUpdated"/> работал бы на
        /// каждую раскладку глубокого дизайнера.
        /// </summary>
        private static void HookBreadCrumbLayout(ListBox breadCrumb, StudioThemeColors palette)
        {
            BreadCrumbHook hook = BreadCrumbHooks.GetValue(breadCrumb, key => new BreadCrumbHook());
            hook.Palette = palette;
            hook.Fingerprint = BreadCrumbFingerprint(breadCrumb);

            if (hook.LayoutSubscribed)
            {
                return;
            }

            hook.LayoutSubscribed = true;

            breadCrumb.LayoutUpdated += (sender, args) =>
            {
                if (!BreadCrumbHooks.TryGetValue(breadCrumb, out BreadCrumbHook current) || current.Pending)
                {
                    return;
                }

                int fingerprint = BreadCrumbFingerprint(breadCrumb);
                if (fingerprint == current.Fingerprint)
                {
                    return;
                }

                current.Fingerprint = fingerprint;
                current.Pending = true;

                // Отложенно: проход меняет шрифт контейнеров, то есть сам вызывает раскладку.
                _ = breadCrumb.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        current.Pending = false;

                        if (BreadCrumbHooks.TryGetValue(breadCrumb, out BreadCrumbHook latest))
                        {
                            RefreshBreadCrumb(breadCrumb, latest.Palette);
                            latest.Fingerprint = BreadCrumbFingerprint(breadCrumb);
                        }
                    }),
                    DispatcherPriority.Background);
            };
        }

        /// <summary>Отпечаток поддерева крошки: число крошек и визуальных детей (глубина ограничена).</summary>
        private static int BreadCrumbFingerprint(ListBox breadCrumb)
        {
            int count = 0;
            CountVisuals(breadCrumb, ref count, 0);
            return (breadCrumb.Items.Count * 397) ^ count;
        }

        private static void CountVisuals(DependencyObject root, ref int count, int depth)
        {
            if (root == null || depth > 8 || count > 512 || !(root is Visual))
            {
                return;
            }

            int children = VisualTreeHelper.GetChildrenCount(root);
            count += children;

            for (int index = 0; index < children; index++)
            {
                CountVisuals(VisualTreeHelper.GetChild(root, index), ref count, depth + 1);
            }
        }

        /// <summary>Состояние подписки на генератор элементов крошки.</summary>
        private sealed class BreadCrumbHook
        {
            public StudioThemeColors Palette { get; set; }

            public bool Subscribed { get; set; }

            /// <summary>Подписка на <see cref="FrameworkElement.LayoutUpdated"/> ставится один раз.</summary>
            public bool LayoutSubscribed { get; set; }

            /// <summary>Отложенный проход уже запланирован — защита от повторной постановки.</summary>
            public bool Pending { get; set; }

            /// <summary>Отпечаток поддерева на момент последнего прохода по раскладке.</summary>
            public int Fingerprint { get; set; }
        }
    }
}
