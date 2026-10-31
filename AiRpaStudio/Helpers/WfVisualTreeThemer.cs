using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Тематизирует нативные WF-контролы (<c>ToolboxControl</c>, <c>OutlineView</c> и т.п.),
    /// которые не подхватывают стили WPF/MaterialDesign приложения, обходом визуального дерева
    /// (этап 6 плана docs\Vs2026_Theme_Plan.md, F-01/F-02).
    /// <para>
    /// В отличие от прежней реализации: значения берутся из <see cref="StudioPalette"/> (не из акцента
    /// MaterialDesign), состояние элемента передаётся триггерами стиля (<c>IsMouseOver</c> → Hover,
    /// <c>IsSelected</c> → Selected), а не разовым присваиванием <c>Foreground</c>, и шрифт — Segoe UI.
    /// </para>
    /// <para>
    /// Стиль элемента дерева ставится через <see cref="ItemsControl.ItemContainerStyle"/> (наследуется
    /// контейнерами, созданными позже, — важно для виртуализованного Toolbox) и дополнительно на уже
    /// материализованные <see cref="TreeViewItem"/>.
    /// </para>
    /// </summary>
    internal static class WfVisualTreeThemer
    {
        private static readonly ConditionalWeakTable<FrameworkElement, object> LoadedHooks =
            new ConditionalWeakTable<FrameworkElement, object>();

        private static Style DefaultItemStyle;
        private static StudioThemeColors DefaultItemStylePalette;

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

            RoutedEventHandler handler = null;
            handler = (sender, args) =>
            {
                element.Loaded -= handler;
                Apply(element, palette);
            };

            element.Loaded += handler;
        }

        /// <summary>Сколько раз на элемент подписывались через <see cref="ApplyWhenLoaded"/> (для тестов идемпотентности).</summary>
        public static int LoadedHookCount(FrameworkElement element)
        {
            if (element == null)
            {
                return 0;
            }

            return LoadedHooks.TryGetValue(element, out _) ? 1 : 0;
        }

        /// <summary>
        /// Стиль элемента дерева: нейтральные состояния вместо системного выделения
        /// (<c>SystemColors.HighlightBrushKey</c> до этапа 2 был акцентом MaterialDesign).
        /// </summary>
        internal static Style CreateItemContainerStyle(Style baseStyle, StudioThemeColors palette)
        {
            if (baseStyle != null && WfThemeStyles.IsThemed(baseStyle))
            {
                return baseStyle;
            }

            if (baseStyle != null && baseStyle.TargetType != null && !baseStyle.TargetType.IsAssignableFrom(typeof(TreeViewItem)))
            {
                // Стиль с чужим TargetType (наследник TreeViewItem) обернуть нельзя: WPF бросит
                // на несовпадении TargetType, а подмена стиля потеряла бы шаблон элемента WF.
                return baseStyle;
            }

            if (baseStyle == null && DefaultItemStyle != null && ReferenceEquals(DefaultItemStylePalette, palette))
            {
                return DefaultItemStyle;
            }

            Style style = WfThemeStyles.Wrap(typeof(TreeViewItem), baseStyle);

            style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Hover)));
            style.Triggers.Add(hover);

            Trigger selected = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Selected)));
            style.Triggers.Add(selected);

            WfThemeStyles.MarkThemed(style);

            if (baseStyle == null)
            {
                // Элементы без собственного стиля переиспользуют один экземпляр (нет роста числа стилей).
                DefaultItemStyle = style;
                DefaultItemStylePalette = palette;
            }

            return style;
        }

        private static void ThemeTreeView(TreeView treeView, StudioThemeColors palette, Brush surface, Brush separator, Brush foreground)
        {
            treeView.Background = surface;
            treeView.BorderBrush = separator;
            treeView.Foreground = foreground;
            treeView.FontFamily = WfThemeStyles.Font;
            treeView.ItemContainerStyle = CreateItemContainerStyle(treeView.ItemContainerStyle, palette);
        }

        private static void ThemeTreeViewItem(TreeViewItem item, StudioThemeColors palette, Brush foreground)
        {
            item.Foreground = foreground;
            item.FontFamily = WfThemeStyles.Font;

            if (item.Style == null || WfThemeStyles.IsThemed(item.Style))
            {
                // Уже темизированные элементы переиспользуют готовый стиль: цепочка BasedOn не растёт.
                item.Style = CreateItemContainerStyle(item.Style, palette);
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

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

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
