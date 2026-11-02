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
using Material.Icons;

namespace RPA.Helpers
{
    /// <summary>
    /// Темизация нативного <c>PropertyInspectorView</c> (этап 6 плана docs\Vs2026_Theme_Plan.md, F-03).
    /// <para>
    /// Что изменилось по сравнению с прежней версией:
    /// </para>
    /// <list type="bullet">
    /// <item>элементы ищутся <b>по именам</b> (<c>_propertyToolBar</c>, <c>PART_ByCategoryViewButton</c>,
    /// <c>PART_AlphaViewButton</c>, <c>PART_SearchClearButton</c>), а не позиционным обходом
    /// «первый Border → первый Grid → первый StackPanel»;</item>
    /// <item>все цвета — из <see cref="StudioPalette"/>, акцент MaterialDesign не используется,
    /// разделители — hairline <c>Separator</c>;</item>
    /// <item>состояния кнопок заданы триггерами стиля (Binding на <c>IsMouseOver</c>/<c>IsPressed</c>/<c>IsChecked</c>),
    /// а не обработчиками событий: повторное применение не накапливает подписки;</item>
    /// <item>повторный вызов на том же контроле (смена темы) перекрашивает его без пересоздания —
    /// хранится только исходный стиль поля поиска (<see cref="OriginalTextBoxStyles"/>).</item>
    /// </list>
    /// </summary>
    internal class PropertyWindowStyleHelper
    {
        public const string PropertyToolBarName = "_propertyToolBar";
        public const string ByCategoryButtonName = "PART_ByCategoryViewButton";
        public const string AlphaViewButtonName = "PART_AlphaViewButton";
        public const string SearchClearButtonName = "PART_SearchClearButton";

        public const double ToolButtonSize = 28d;
        public const double ClearButtonSize = 24d;
        public const double SearchTextBoxHeight = 24d;

        /// <summary>Отступ иконки внутри кнопки: 28 → 20, 24 → 16.</summary>
        private const double IconPadding = 8d;

        /// <summary>
        /// Исходные (до темизации) стили полей поиска. Хранятся слабо, чтобы повторное применение
        /// с другой палитрой строило стиль от исходного, а не от предыдущего результата.
        /// </summary>
        private static readonly ConditionalWeakTable<TextBox, Style> OriginalTextBoxStyles =
            new ConditionalWeakTable<TextBox, Style>();

        private readonly MaterialIconDataProvider materialIconData = new MaterialIconDataProvider();
        private ControlTemplate flatButtonTemplate;

        /// <summary>Палитра последнего применения (нужна для повторной темизации и тестов).</summary>
        public StudioThemeColors Palette { get; private set; } = StudioPalette.Dark;

        /// <summary>Применяет палитру, определённую по ресурсам приложения.</summary>
        public void SetPropertyStyles(UIElement propertyInspector)
        {
            SetPropertyStyles(propertyInspector, StudioPalette.Detect(Application.Current?.Resources));
        }

        /// <summary>Применяет конкретную палитру (идемпотентно, без пересоздания контрола).</summary>
        public void SetPropertyStyles(UIElement propertyInspector, StudioThemeColors palette)
        {
            if (palette == null)
            {
                return;
            }

            Palette = palette;

            if (propertyInspector is FrameworkElement inspectorElement)
            {
                // Ключи палитры кладутся в ресурсы самого инспектора: его шаблоны читают собственные
                // TextBrushKey/BackgroundBrushKey/FontFamilyKey и перекрывают словарь приложения,
                // поэтому без этого шага окно «Свойства» оставалось частично светлым.
                WfDesignerTheme.ApplyToApplication(inspectorElement.Resources, palette);
                Attach(inspectorElement);
            }

            if (propertyInspector == null)
            {
                return;
            }

            ApplyRoot(propertyInspector);
            ApplyToolBar(propertyInspector);
            ApplyIconButton(propertyInspector, ByCategoryButtonName, MaterialIconKind.ShapePlusOutline, ToolButtonSize);
            ApplyIconButton(propertyInspector, AlphaViewButtonName, MaterialIconKind.OrderAlphabeticalAscending, ToolButtonSize);
            ApplyIconButton(propertyInspector, SearchClearButtonName, MaterialIconKind.Close, ClearButtonSize);
            ApplySearchTextBox(propertyInspector);
            ApplyValueEditors(propertyInspector);
            ApplySeparators(propertyInspector);
            ApplyFont(propertyInspector);
        }

        /// <summary>Достаёт данные пути из SVG-строки Material.Icons (или возвращает строку как есть).</summary>
        internal static string ExtractPathData(string svgData)
        {
            if (string.IsNullOrWhiteSpace(svgData))
            {
                return null;
            }

            string value = svgData.Trim();
            if (!value.Contains("<path"))
            {
                return value;
            }

            int start = value.IndexOf("d=\"", StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += 3;
            int end = value.IndexOf('"', start);
            return end > start ? value.Substring(start, end - start) : null;
        }

        // --- Корень и панель инструментов. ---

        private void ApplyRoot(UIElement propertyInspector)
        {
            SetBackground(propertyInspector, WfThemeStyles.Brush(Palette.Surface));

            // Рамка самого контрола не нужна: вокруг панели уже есть тонкая рамка AvalonDock (правило 6), вторая линия была бы двойной.
            foreach (Border border in FindVisualChildren<Border>(propertyInspector).Take(1))
            {
                border.BorderThickness = new Thickness(0);
            }
        }

        private void ApplyToolBar(UIElement propertyInspector)
        {
            if (!(FindByName<FrameworkElement>(propertyInspector, PropertyToolBarName) is FrameworkElement toolBar))
            {
                return;
            }

            SolidColorBrush surface = WfThemeStyles.Brush(Palette.Surface);
            SetBackground(toolBar, surface);

            foreach (Border border in FindVisualChildren<Border>(toolBar).Take(1))
            {
                border.Background = surface;
            }

            foreach (Grid grid in FindVisualChildren<Grid>(toolBar).Take(1))
            {
                grid.Background = surface;
            }
        }

        // --- Кнопки панели (иконки Material.Icons). ---

        private void ApplyIconButton(DependencyObject propertyInspector, string name, MaterialIconKind icon, double size)
        {
            if (!(FindByName<Control>(propertyInspector, name) is Control button))
            {
                return;
            }

            button.Width = size;
            button.Height = size;
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0);
            button.FontFamily = WfThemeStyles.Font;

            if (button is ContentControl contentControl)
            {
                // Контент пересоздаётся, сам контрол — никогда: обработчиков не добавляем.
                contentControl.Content = CreateIconHost(button as ButtonBase, icon, size);
            }

            if (button is ButtonBase buttonBase)
            {
                buttonBase.Template = FlatButtonTemplate;
            }
        }

        private Border CreateIconHost(ButtonBase button, MaterialIconKind icon, double size)
        {
            Border host = new Border
            {
                Width = size,
                Height = size,
                BorderThickness = new Thickness(1),
                BorderBrush = WfThemeStyles.Brush(Palette.Separator),
                CornerRadius = new CornerRadius(StudioPalette.ButtonCornerRadius),
                Child = CreateIcon(icon, Math.Max(size - IconPadding, 8d), Palette.Foreground),
                Style = CreateStateStyle(button)
            };

            return host;
        }

        /// <summary>Состояния кнопки: hover → Hover, нажатие/включено → Selected (без акцента).</summary>
        private Style CreateStateStyle(ButtonBase button)
        {
            Style style = new Style(typeof(Border));

            if (button == null)
            {
                return style;
            }

            style.Triggers.Add(CreateStateTrigger(button, nameof(UIElement.IsMouseOver), true, Palette.Hover));
            style.Triggers.Add(CreateStateTrigger(button, nameof(ButtonBase.IsPressed), true, Palette.Selected));

            if (button is ToggleButton)
            {
                style.Triggers.Add(CreateStateTrigger(button, nameof(ToggleButton.IsChecked), true, Palette.Selected));
            }

            return style;
        }

        private static DataTrigger CreateStateTrigger(object source, string path, object value, Color background)
        {
            DataTrigger trigger = new DataTrigger
            {
                Binding = new Binding(path) { Source = source },
                Value = value
            };

            trigger.Setters.Add(new Setter(Border.BackgroundProperty, WfThemeStyles.Brush(background)));
            return trigger;
        }

        private UIElement CreateIcon(MaterialIconKind kind, double size, Color color)
        {
            string pathData = ExtractPathData(materialIconData.ProvideData(kind));
            if (pathData == null)
            {
                return null;
            }

            Geometry geometry;
            try
            {
                geometry = Geometry.Parse(pathData);
            }
            catch (FormatException)
            {
                return null;
            }

            Path path = new Path
            {
                Data = geometry,
                Fill = WfThemeStyles.Brush(color),
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Child = path
            };
        }

        /// <summary>Кнопка без собственной хромировки: только контент (иконка в рамке).</summary>
        private ControlTemplate FlatButtonTemplate
        {
            get
            {
                if (flatButtonTemplate == null)
                {
                    FrameworkElementFactory factory = new FrameworkElementFactory(typeof(ContentPresenter));
                    factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

                    ControlTemplate template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = factory };
                    template.Seal();
                    flatButtonTemplate = template;
                }

                return flatButtonTemplate;
            }
        }

        // --- Поиск и шрифт. ---

        private void ApplySearchTextBox(DependencyObject propertyInspector)
        {
            TextBox searchTextBox = propertyInspector as TextBox ?? FindVisualChildren<TextBox>(propertyInspector).FirstOrDefault();
            if (searchTextBox == null)
            {
                return;
            }

            searchTextBox.Height = SearchTextBoxHeight;
            searchTextBox.BorderThickness = new Thickness(1);
            searchTextBox.VerticalContentAlignment = VerticalAlignment.Center;
            searchTextBox.FontFamily = WfThemeStyles.Font;

            Style style = WfThemeStyles.Wrap(typeof(TextBox), GetOriginalStyle(searchTextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(Palette.Surface)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(Palette.Foreground)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, WfThemeStyles.Brush(Palette.Separator)));
            style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, WfThemeStyles.Brush(Palette.Foreground)));

            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, WfThemeStyles.Brush(Palette.SeparatorStrong)));
            style.Triggers.Add(focus);

            WfThemeStyles.MarkThemed(style);
            searchTextBox.Style = style;
        }

        /// <summary>Стиль поля поиска до темизации (переживает повторные применения).</summary>
        private static Style GetOriginalStyle(TextBox textBox)
        {
            Style current = textBox.Style;
            if (current == null)
            {
                return null;
            }

            if (!WfThemeStyles.IsThemed(current))
            {
                _ = OriginalTextBoxStyles.Remove(textBox);
                OriginalTextBoxStyles.Add(textBox, current);
                return current;
            }

            return OriginalTextBoxStyles.TryGetValue(textBox, out Style original) ? original : null;
        }

        private void ApplySeparators(DependencyObject propertyInspector)
        {
            SolidColorBrush separator = WfThemeStyles.Brush(Palette.Separator);

            foreach (Border border in FindVisualChildren<Border>(propertyInspector))
            {
                if (IsSeparator(border.Name, border.Width))
                {
                    border.Background = separator;
                    border.BorderBrush = separator;
                }
            }

            foreach (Rectangle rectangle in FindVisualChildren<Rectangle>(propertyInspector))
            {
                if (IsSeparator(rectangle.Name, rectangle.Width))
                {
                    rectangle.Fill = separator;
                }
            }
        }

        private static bool IsSeparator(string name, double width)
        {
            return (width > 0 && width <= 2) || (name != null
                && (name.IndexOf("Separator", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Divider", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void ApplyFont(DependencyObject propertyInspector)
        {
            foreach (Control control in FindVisualChildren<Control>(propertyInspector))
            {
                control.FontFamily = WfThemeStyles.Font;
            }

            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(propertyInspector))
            {
                textBlock.FontFamily = WfThemeStyles.Font;
            }
        }

        // --- Повторное применение после пересоздания содержимого (этап 12). ---

        /// <summary>
        /// Подписка на пересоздание содержимого инспектора. WF строит строки свойств заново при выборе
        /// активности, развороте категории и скролле (виртуализация), и новые строки приходят без кистей —
        /// поэтому на каждом <see cref="FrameworkElement.LayoutUpdated"/> сверяется дешёвый отпечаток дерева,
        /// и при изменении тема применяется повторно. Подписка ставится один раз на элемент.
        /// </summary>
        public void Attach(FrameworkElement propertyInspector)
        {
            if (propertyInspector == null)
            {
                return;
            }

            if (Attachments.TryGetValue(propertyInspector, out Attachment attachment))
            {
                // Тот же инспектор темизирует другой помощник (MainView / DocGroupHelper) — берём его палитру.
                attachment.Owner = this;
                return;
            }

            Attachments.Add(propertyInspector, new Attachment
            {
                Owner = this,
                Signature = ComputeSignature(propertyInspector)
            });

            propertyInspector.LayoutUpdated += OnInspectorLayoutUpdated;
            propertyInspector.Unloaded += OnInspectorUnloaded;
        }

        /// <summary>Сколько инспекторов сейчас под наблюдением (для тестов идемпотентности).</summary>
        public static bool IsAttached(FrameworkElement propertyInspector)
        {
            return propertyInspector != null && Attachments.TryGetValue(propertyInspector, out _);
        }

        private static void OnInspectorLayoutUpdated(object sender, EventArgs e)
        {
            if (!(sender is FrameworkElement inspector)
                || !Attachments.TryGetValue(inspector, out Attachment attachment))
            {
                return;
            }

            PropertyWindowStyleHelper owner = attachment.Owner;
            if (owner == null || attachment.Pending)
            {
                return;
            }

            if (ComputeSignature(inspector) == attachment.Signature)
            {
                return;
            }

            attachment.Pending = true;
            Dispatcher dispatcher = inspector.Dispatcher;
            _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                attachment.Pending = false;
                if (!Attachments.TryGetValue(inspector, out Attachment current))
                {
                    return;
                }

                // Отпечаток снимается до применения: применение само меняет свойства и не должно
                // запускать бесконечную цепочку LayoutUpdated → повторная темизация.
                current.Signature = ComputeSignature(inspector);
                current.Owner?.SetPropertyStyles(inspector, current.Owner.Palette);
            }));
        }

        private static void OnInspectorUnloaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement inspector))
            {
                return;
            }

            inspector.LayoutUpdated -= OnInspectorLayoutUpdated;
            inspector.Unloaded -= OnInspectorUnloaded;
            _ = Attachments.Remove(inspector);
        }

        /// <summary>
        /// Дешёвый отпечаток дерева инспектора: количество контейнеров и текстовых узлов.
        /// Меняется, когда WF пересоздаёт строку свойств или подменяет редактор значения.
        /// </summary>
        internal static int ComputeSignature(DependencyObject propertyInspector)
        {
            if (propertyInspector == null)
            {
                return 0;
            }

            int controls = 0;
            foreach (Control control in FindVisualChildren<Control>(propertyInspector))
            {
                controls++;
            }

            int textBlocks = 0;
            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(propertyInspector))
            {
                textBlocks++;
            }

            return (controls * 397) ^ textBlocks;
        }

        // --- Редакторы значений строк свойств. ---

        /// <summary>
        /// Поля и списки строк «Свойства»: WF рисует редакторы значения своим шаблоном, поэтому цвета
        /// приходят из системных кистей (светлый фон/тёмный текст). Красим их палитрой студии.
        /// </summary>
        private void ApplyValueEditors(DependencyObject propertyInspector)
        {
            SolidColorBrush surface = WfThemeStyles.Brush(Palette.Surface);
            SolidColorBrush foreground = WfThemeStyles.Brush(Palette.Foreground);
            SolidColorBrush separator = WfThemeStyles.Brush(Palette.Separator);

            foreach (TextBox textBox in FindVisualChildren<TextBox>(propertyInspector))
            {
                textBox.Background = surface;
                textBox.Foreground = foreground;
                textBox.BorderBrush = separator;
                textBox.CaretBrush = foreground;
                textBox.FontFamily = WfThemeStyles.Font;
            }

            foreach (ComboBox comboBox in FindVisualChildren<ComboBox>(propertyInspector))
            {
                comboBox.Background = surface;
                comboBox.Foreground = foreground;
                comboBox.BorderBrush = separator;
                comboBox.FontFamily = WfThemeStyles.Font;
            }

            foreach (ComboBoxItem item in FindVisualChildren<ComboBoxItem>(propertyInspector))
            {
                item.Background = surface;
                item.Foreground = foreground;
                item.FontFamily = WfThemeStyles.Font;
            }

            foreach (ListBox listBox in FindVisualChildren<ListBox>(propertyInspector))
            {
                listBox.Background = surface;
                listBox.Foreground = foreground;
            }

            foreach (ListBoxItem item in FindVisualChildren<ListBoxItem>(propertyInspector))
            {
                item.Foreground = foreground;
                item.FontFamily = WfThemeStyles.Font;
            }

            foreach (CheckBox checkBox in FindVisualChildren<CheckBox>(propertyInspector))
            {
                checkBox.Foreground = foreground;
                checkBox.Background = Brushes.Transparent;
                checkBox.FontFamily = WfThemeStyles.Font;
            }

            foreach (Expander expander in FindVisualChildren<Expander>(propertyInspector))
            {
                expander.Foreground = foreground;
                expander.Background = surface;
                expander.FontFamily = WfThemeStyles.Font;
            }
        }

        /// <summary>Состояние наблюдения за одним инспектором (палитра-владелец и отпечаток дерева).</summary>
        private sealed class Attachment
        {
            public PropertyWindowStyleHelper Owner { get; set; }

            public int Signature { get; set; }

            public bool Pending { get; set; }
        }

        private static readonly ConditionalWeakTable<FrameworkElement, Attachment> Attachments =
            new ConditionalWeakTable<FrameworkElement, Attachment>();

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

        /// <summary>Поиск по имени: визуальное дерево, затем логическое (шаблон контрола может быть ещё не применён).</summary>
        internal static T FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (root is T rootElement && rootElement.Name == name)
            {
                return rootElement;
            }

            foreach (T element in FindVisualChildren<T>(root))
            {
                if (element.Name == name)
                {
                    return element;
                }
            }

            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject dependencyObject)
                {
                    T found = FindByName<T>(dependencyObject, name);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            // Обход через XamlHelper: он пропускает не-визуальные узлы (RowDefinition/ColumnDefinition,
            // которые LogicalTreeHelper отдаёт как детей Grid) вместо ArgumentException.
            return XamlHelper.FindVisualChildren<T>(parent);
        }
    }
}
