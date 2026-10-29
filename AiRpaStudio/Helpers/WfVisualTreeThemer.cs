using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Тематизирует нативные WF-контролы (ToolboxControl, OutlineView и т.п.),
    /// которые не подхватывают стили WPF/MaterialDesign приложения, обходом визуального дерева.
    /// </summary>
    internal static class WfVisualTreeThemer
    {
        public static void Apply(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            MaterialThemeColors colors = MaterialThemePalette.Resolve(Application.Current?.Resources);
            Apply(root, colors);
        }

        public static void Apply(DependencyObject root, MaterialThemeColors colors)
        {
            if (root == null)
            {
                return;
            }

            SetBackground(root, colors.Background);

            foreach (TreeView treeView in FindVisualChildren<TreeView>(root))
            {
                treeView.Background = new SolidColorBrush(colors.Background);
                treeView.BorderBrush = new SolidColorBrush(colors.Border);
                treeView.Foreground = new SolidColorBrush(colors.Foreground);
            }

            foreach (TreeViewItem item in FindVisualChildren<TreeViewItem>(root))
            {
                item.Foreground = new SolidColorBrush(colors.Foreground);
            }

            foreach (TextBox textBox in FindVisualChildren<TextBox>(root))
            {
                textBox.Background = new SolidColorBrush(colors.Background);
                textBox.BorderBrush = new SolidColorBrush(colors.Border);
                textBox.Foreground = new SolidColorBrush(colors.Foreground);
                textBox.CaretBrush = new SolidColorBrush(colors.Foreground);
            }
        }

        private static void SetBackground(DependencyObject element, Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);

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