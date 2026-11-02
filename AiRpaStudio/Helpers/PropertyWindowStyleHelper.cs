using System;
using System.Activities.Presentation.PropertyEditing;
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
    /// <item>панель плоская: у переключателей «по категориям»/«алфавитный» скруглений нет (радиус 0),
    /// у очистки поиска рамка снята вовсе, а крестик меньше прежнего в 1,5 раза
    /// (<see cref="ClearIconSize"/>) — размеры кнопок при этом не меняются;</item>
    /// <item>кнопка редактирования значения «…» в строках свойств (WF <c>EditModeSwitchButton</c>) лишена
    /// системной хромировки: без скруглений и без тени (<see cref="ApplyEditModeButtons"/>).</item>
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

        /// <summary>Размер иконки переключателей «по категориям»/«алфавитный»: 28 − 8 = 20 px.</summary>
        public const double ToolButtonIconSize = ToolButtonSize - IconPadding;

        /// <summary>
        /// Размер крестика очистки поиска: раньше был 16 px (24 − <see cref="IconPadding"/>), по требованию
        /// «крестик в 1,5 раза меньше» — 16 / 1,5 ≈ 10,7, округлено до 11 px. Размер самой кнопки (24×24)
        /// не меняется: уменьшается только глиф.
        /// </summary>
        public const double ClearIconSize = 11d;

        /// <summary>
        /// Отступ иконки внутри кнопки: 28 → 20. У кнопки очистки поиска глиф задан отдельно
        /// (<see cref="ClearIconSize"/>) — там этот отступ не применяется.
        /// </summary>
        private const double IconPadding = 8d;

        /// <summary>
        /// Исходные (до темизации) стили полей поиска. Хранятся слабо, чтобы повторное применение
        /// с другой палитрой строило стиль от исходного, а не от предыдущего результата.
        /// </summary>
        private static readonly ConditionalWeakTable<TextBox, Style> OriginalTextBoxStyles =
            new ConditionalWeakTable<TextBox, Style>();

        private readonly MaterialIconDataProvider materialIconData = new MaterialIconDataProvider();
        private static ControlTemplate flatButtonTemplate;

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
            // Переключатели видов: hairline-рамка остаётся, скруглений нет (панель плоская).
            ApplyIconButton(propertyInspector, ByCategoryButtonName, MaterialIconKind.ShapePlusOutline, ToolButtonSize, ToolButtonIconSize, framed: true);
            ApplyIconButton(propertyInspector, AlphaViewButtonName, MaterialIconKind.OrderAlphabeticalAscending, ToolButtonSize, ToolButtonIconSize, framed: true);
            // Крестик очистки поиска: рамки нет вовсе, глиф меньше в 1,5 раза, кнопка остаётся 24×24.
            ApplyIconButton(propertyInspector, SearchClearButtonName, MaterialIconKind.Close, ClearButtonSize, ClearIconSize, framed: false);
            ApplySearchTextBox(propertyInspector);
            ApplyValueEditors(propertyInspector);
            ApplyEditModeButtons(propertyInspector);
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

        private void ApplyIconButton(
            DependencyObject propertyInspector,
            string name,
            MaterialIconKind icon,
            double size,
            double iconSize,
            bool framed)
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
                contentControl.Content = CreateIconHost(button as ButtonBase, icon, size, iconSize, framed);
            }

            if (button is ButtonBase buttonBase)
            {
                buttonBase.Template = FlatButtonTemplate;
            }
        }

        /// <summary>
        /// Плашка иконки кнопки панели. Скруглений нет: панель «Свойства» плоская, поэтому
        /// <see cref="StudioPalette.ButtonCornerRadius"/> здесь не применяется (общий токен студии не тронут).
        /// <paramref name="framed"/> = <c>false</c> — рамки нет вовсе (кнопка очистки поиска),
        /// <paramref name="iconSize"/> задаёт размер глифа независимо от размера кнопки.
        /// </summary>
        private Border CreateIconHost(ButtonBase button, MaterialIconKind icon, double size, double iconSize, bool framed)
        {
            Border host = new Border
            {
                Width = size,
                Height = size,
                BorderThickness = framed ? new Thickness(1) : new Thickness(0),
                BorderBrush = framed ? WfThemeStyles.Brush(Palette.Separator) : Brushes.Transparent,
                CornerRadius = new CornerRadius(0),
                Child = CreateIcon(icon, Math.Max(iconSize, 8d), Palette.Foreground),
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

        /// <summary>
        /// Кнопка без собственной хромировки: только контент. Используется и для кнопок панели
        /// (иконка в рамке), и как плоская замена системной хромировки кнопки «…».
        /// </summary>
        private static ControlTemplate FlatButtonTemplate
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

        // --- Кнопка «…» строк свойств (WF EditModeSwitchButton). ---

        /// <summary>
        /// Кнопка редактирования значения «…» (<c>EditModeSwitchButton</c> из
        /// <c>System.Activities.Presentation.PropertyEditing</c>) приходит с системной хромировкой: скруглённой
        /// рамкой и тенью, из-за чего в плоской панели «Свойства» она выглядит чужеродно. Проход снимает
        /// хромировку локальными значениями. Строки, созданные позже (выбор другой активности, разворот
        /// категории, скролл), подхватываются обычным повторным применением через <see cref="Attach"/>.
        /// </summary>
        private void ApplyEditModeButtons(DependencyObject propertyInspector)
        {
            foreach (EditModeSwitchButton button in FindAll<EditModeSwitchButton>(propertyInspector))
            {
                MakeFlatButtonChrome(button);
            }
        }

        /// <summary>
        /// Плоская кнопка «…»: без рамки, без скруглений и без тени. Размеры, отступы и содержимое («…»)
        /// не меняются — снимается только оформление, поэтому повторный вызов безопасен.
        /// </summary>
        internal static void MakeFlatButtonChrome(Control button)
        {
            if (button == null)
            {
                return;
            }

            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Effect = null;

            // Строка могла быть создана только что: без развёрнутого шаблона обходить нечего.
            _ = button.ApplyTemplate();

            bool chromeIsBorder = FlattenChrome(button);

            // Хромировку рисует не Border, а системный ButtonChrome (Aero2): у него нет ни CornerRadius,
            // ни Border-потомка, поэтому скругление и тень снимаются только собственным плоским шаблоном.
            // Ставится он лишь когда глиф приходит из Content — иначе содержимое шаблона потерялось бы.
            if (!chromeIsBorder && button is ContentControl contentControl && contentControl.Content != null)
            {
                button.Template = FlatButtonTemplate;
            }
        }

        /// <summary>
        /// Обнуляет скругления, рамки и тени внутри шаблона кнопки.
        /// Возвращает <c>true</c>, если найден Border-хром (т.е. скругление рисует он, а не системная хромировка).
        /// </summary>
        private static bool FlattenChrome(DependencyObject parent)
        {
            bool chromeIsBorder = false;

            foreach (DependencyObject child in XamlHelper.Children(parent))
            {
                if (!XamlHelper.IsVisual(child))
                {
                    continue;
                }

                if (child is Border border)
                {
                    chromeIsBorder = true;
                    border.CornerRadius = new CornerRadius(0);
                    border.BorderThickness = new Thickness(0);
                    border.BorderBrush = Brushes.Transparent;
                    border.Background = Brushes.Transparent;
                }

                if (child is FrameworkElement element)
                {
                    element.Effect = null;
                }

                if (child is Control control)
                {
                    control.BorderThickness = new Thickness(0);
                    control.BorderBrush = Brushes.Transparent;
                    control.Effect = null;
                }

                if (FlattenChrome(child))
                {
                    chromeIsBorder = true;
                }
            }

            return chromeIsBorder;
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

            // Строки свойств WF пересобирает при выборе активности, развороте категории и скролле, поэтому
            // комбобоксы строк (Scope, Modifiers и прочие enum-свойства) появляются уже после первого прохода
            // и остаются с системной белой плашкой. Клик/фокус внутри панели — гарантированная точка, где
            // строки существуют: комбобоксы доводятся до палитры прямо перед открытием их списка.
            propertyInspector.PreviewMouseDown += OnInspectorInteraction;
            propertyInspector.GotKeyboardFocus += OnInspectorInteraction;
        }

        /// <summary>Сколько инспекторов сейчас под наблюдением (для тестов идемпотентности).</summary>
        public static bool IsAttached(FrameworkElement propertyInspector)
        {
            return propertyInspector != null && Attachments.TryGetValue(propertyInspector, out _);
        }

        private static void OnInspectorInteraction(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement inspector && Attachments.TryGetValue(inspector, out Attachment attachment))
            {
                // Только комбобоксы: полный SetPropertyStyles на каждый клик пересоздавал бы иконки панели.
                WfVisualTreeThemer.ThemeComboBoxes(inspector, attachment.Owner?.Palette ?? StudioPalette.Dark);
            }
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
            inspector.PreviewMouseDown -= OnInspectorInteraction;
            inspector.GotKeyboardFocus -= OnInspectorInteraction;
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
        /// <para>
        /// Комбобоксы (enum-свойства: Scope, Modifiers, True/False) отдаются
        /// <see cref="WfVisualTreeThemer.ThemeComboBoxes"/>: только он ставит стиль элементов списка и
        /// гасит плашки шаблона Aero2 в popup, которого в дереве инспектора нет.
        /// </para>
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

            // Комбобоксы строк (enum-свойства: Scope, Modifiers, True/False). Шаблон Aero2 рисует под их
            // текстом опаковую белую плашку, а список раскрытия живёт в popup — вне визуального дерева
            // инспектора, поэтому прежний обход ComboBoxItem внутри инспектора «чёрные буквы на тёмном
            // фоне» не лечил вовсе: элементов списка в дереве ещё нет. Темизация — тем же механизмом,
            // что shell bar и таблицы: кисти, стиль элементов списка и подписка на раскрытие.
            WfVisualTreeThemer.ThemeComboBoxes(propertyInspector, Palette);
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

        /// <summary>
        /// Поиск по типу в логическом и визуальном дереве: строка свойств может быть создана, но ещё не
        /// развёрнута шаблоном, и тогда элемент виден только в логическом дереве.
        /// </summary>
        private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            return root == null ? Enumerable.Empty<T>() : FindAllIterator<T>(root);
        }

        private static IEnumerable<T> FindAllIterator<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (DependencyObject child in XamlHelper.Children(parent))
            {
                // RowDefinition/ColumnDefinition и прочие не-визуальные узлы: их детей обходить нельзя.
                if (!XamlHelper.IsVisual(child))
                {
                    continue;
                }

                if (child is T match)
                {
                    yield return match;
                }

                foreach (T nested in FindAllIterator<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            // Обход через XamlHelper: он пропускает не-визуальные узлы (RowDefinition/ColumnDefinition,
            // которые LogicalTreeHelper отдаёт как детей Grid) вместо ArgumentException.
            return XamlHelper.FindVisualChildren<T>(parent);
        }
    }
}
