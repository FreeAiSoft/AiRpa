using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Activities.Presentation;

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
            ApplyToApplication(Application.Current?.Resources, StudioPalette.Detect(Application.Current?.Resources));
        }

        /// <summary>
        /// Кладёт ключи дизайнера в указанный словарь. Словарь-владелец создаётся один раз и дальше
        /// только мутируется: <c>DynamicResource</c>-ссылки в уже загруженных шаблонах WF пересчитываются
        /// на каждое изменение, а число слитых словарей не растёт.
        /// </summary>
        public static void ApplyToApplication(ResourceDictionary target, StudioThemeColors palette)
        {
            if (target == null || palette == null)
            {
                return;
            }

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
            Apply(root, StudioPalette.Detect(Application.Current?.Resources));
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
            SolidColorBrush separatorStrong = WfThemeStyles.Brush(palette.SeparatorStrong);

            // 1) Ключи: в корень документа (ExpressionTextBox, ErrorView) и в сам DesignerView —
            //    его собственные ColorResources иначе перекрывают словарь приложения.
            ApplyToApplication(root.Resources, palette);

            FrameworkElement designerView = FindByName(root, DesignerViewElementName);
            if (designerView != null)
            {
                ApplyToApplication(designerView.Resources, palette);
                SetBackground(designerView, surface);
            }

            // 2) Поверхность: один фон на всю площадь.
            SetBackground(root, surface);

            // 3) Дефолтные цвета WF, пришедшие через StaticResource (не пересчитываются при смене темы).
            ReplaceWorkflowDefaultColors(root, palette);

            // 4) Именованные части дизайнера.
            if (FindByName(root, ShellBarElementName) is StatusBar shellBar)
            {
                shellBar.Background = surface;
                shellBar.BorderBrush = separator;
                shellBar.BorderThickness = new Thickness(0, 1, 0, 0);
                shellBar.Foreground = foreground;
                shellBar.FontFamily = WfThemeStyles.Font;
            }

            if (FindByName(root, BreadCrumbElementName) is ListBox breadCrumb)
            {
                breadCrumb.Background = surface;
                breadCrumb.Foreground = foreground;
                breadCrumb.BorderBrush = separator;
                breadCrumb.BorderThickness = new Thickness(0);
                breadCrumb.FontFamily = WfThemeStyles.Font;
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

            // 5) Текст (подписи активностей, breadcrumb, watermark) и поля ввода.
            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(root))
            {
                textBlock.Foreground = foreground;
            }

            // Поля ввода и скроллбары дизайнера — общий темизатор WF-контролов.
            WfVisualTreeThemer.Apply(root, palette);

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
            var values = new Dictionary<string, object>(StringComparer.Ordinal);

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

            var root = new FrameworkElementFactory(typeof(Grid));
            var placeholder = new FrameworkElementFactory(typeof(AdornedElementPlaceholder));
            var border = new FrameworkElementFactory(typeof(Rectangle));
            border.SetValue(Shape.StrokeProperty, error);
            border.SetValue(Shape.StrokeThicknessProperty, 1d);
            border.SetValue(Shape.FillProperty, Brushes.Transparent);
            border.SetValue(Rectangle.RadiusXProperty, StudioPalette.TabCornerRadius);
            border.SetValue(Rectangle.RadiusYProperty, StudioPalette.TabCornerRadius);
            border.SetValue(UIElement.IsHitTestVisibleProperty, false);

            root.AppendChild(placeholder);
            root.AppendChild(border);

            var template = new ControlTemplate(typeof(Control)) { VisualTree = root };
            template.Seal();
            return template;
        }

        /// <summary>Кисть ошибки: единственный не-нейтральный цвет палитры, зарезервированный под ошибки.</summary>
        internal static SolidColorBrush ErrorBrush(StudioThemeColors palette)
        {
            return WfThemeStyles.Brush((palette ?? StudioPalette.Dark).CloseHoverBackground);
        }

        /// <summary>Контекстное меню редактирования (Вырезать/Копировать/Вставить) на палитре.</summary>
        internal static ContextMenu CreateEditingContextMenu(StudioThemeColors palette)
        {
            StudioThemeColors colors = palette ?? StudioPalette.Dark;

            var menu = new ContextMenu
            {
                StaysOpen = true,
                HasDropShadow = false,
                Background = WfThemeStyles.Brush(colors.Surface),
                Foreground = WfThemeStyles.Brush(colors.Foreground),
                BorderBrush = WfThemeStyles.Brush(colors.SeparatorStrong),
                BorderThickness = new Thickness(1),
                FontFamily = WfThemeStyles.Font
            };

            menu.Items.Add(CreateMenuItem("Вырезать", ApplicationCommands.Cut, colors));
            menu.Items.Add(CreateMenuItem("Копировать", ApplicationCommands.Copy, colors));
            menu.Items.Add(CreateMenuItem("Вставить", ApplicationCommands.Paste, colors));

            return menu;
        }

        private static MenuItem CreateMenuItem(string header, ICommand command, StudioThemeColors palette)
        {
            return new MenuItem
            {
                Header = header,
                Command = command,
                Foreground = WfThemeStyles.Brush(palette.Foreground),
                Background = WfThemeStyles.Brush(palette.Surface),
                BorderBrush = WfThemeStyles.Brush(palette.Separator),
                FontFamily = WfThemeStyles.Font
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
                    target.MergedDictionaries.Remove(merged);
                    target.MergedDictionaries.Add(merged);
                    return merged;
                }
            }

            var theme = new ResourceDictionary { [PaletteDictionaryMarker] = PaletteDictionaryMarker };
            target.MergedDictionaries.Add(theme);
            return theme;
        }

        /// <summary>
        /// Заменяет дефолтные цвета WF, пришедшие в элементы через <c>{StaticResource}</c>
        /// (белая поверхность дизайнера, фон/рамка/подпись элемента активности). Пользовательские
        /// кисти не трогаются: сравнивается точное значение дефолта.
        /// </summary>
        private static void ReplaceWorkflowDefaultColors(DependencyObject root, StudioThemeColors palette)
        {
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush separatorStrong = WfThemeStyles.Brush(palette.SeparatorStrong);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);

            Color[] designerBackgrounds =
            {
                SafeColor(() => WorkflowDesignerColors.DesignerViewBackgroundColor, FallbackDesignerViewBackground),
                FallbackDesignerViewBackground
            };
            Color[] elementBackgrounds =
            {
                SafeColor(() => WorkflowDesignerColors.WorkflowViewElementBackgroundColor, FallbackViewElementBackground),
                FallbackViewElementBackground
            };
            Color[] elementBorders =
            {
                SafeColor(() => WorkflowDesignerColors.WorkflowViewElementBorderColor, FallbackViewElementBorder),
                FallbackViewElementBorder
            };
            Color[] captions =
            {
                SafeColor(() => WorkflowDesignerColors.WorkflowViewElementCaptionColor, FallbackViewElementCaption),
                FallbackViewElementCaption
            };

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

        private static FrameworkElement FindByName(DependencyObject root, string name)
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

            var seen = new HashSet<DependencyObject>();

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
    }
}
