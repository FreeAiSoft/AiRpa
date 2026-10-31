using System.Activities.Presentation.Toolbox;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPA.Helpers;

namespace RPA.Controls
{
    /// <summary>
    /// Interaction logic for ToolboxPanel.xaml
    /// </summary>
    public partial class ToolboxPanel : UserControl
    {
        private ToolboxControl toolboxControl;

        public ToolboxPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Устанавливает ToolboxControl для отображения
        /// </summary>
        public void SetToolboxControl(ToolboxControl toolbox)
        {
            if (toolbox == null)
            {
                return;
            }

            toolboxControl = toolbox;

            toolbox.Resources ??= new ResourceDictionary();

            // Устанавливаем шрифт через стиль для TreeView
            Style treeViewStyle = new Style(typeof(TreeView));
            treeViewStyle.Setters.Add(new Setter(FontFamilyProperty, WfThemeStyles.Font));
            toolbox.Resources[typeof(TreeView)] = treeViewStyle;

            // Очищаем контейнер и добавляем ToolboxControl
            ToolboxContainer.Child = toolbox;

            // Применяем шаблон, чтобы создать визуальное дерево
            _ = toolbox.ApplyTemplate();

            // Пробуем получить TreeView через GetTemplateChild
            if (toolbox.Template?.FindName("PART_Tools", toolbox) is TreeView treeView)
            {
                // Устанавливаем margin 5 на верхней границе
                treeView.Margin = new Thickness(0, 5, 0, 0);

                // Применяем шаблон TreeView
                _ = treeView.ApplyTemplate();

                // Получаем первый дочерний Border через логическое или визуальное дерево
                Border firstBorder = GetFirstChildBorder(treeView);
                if (firstBorder != null)
                {
                    firstBorder.BorderThickness = new Thickness(0);
                }
            }

            WfVisualTreeThemer.ApplyWhenLoaded(toolbox);
        }

        /// <summary>
        /// Повторно применяет палитру студии к нативному ToolboxControl (вызывается при смене темы).
        /// </summary>
        public void ApplyTheme()
        {
            if (toolboxControl != null)
            {
                WfVisualTreeThemer.ApplyWhenLoaded(toolboxControl);
            }
        }

        private static Border GetFirstChildBorder(TreeView treeView)
        {
            // Сначала пробуем через логическое дерево
            IEnumerable<Border> logicalChildren = LogicalTreeHelper.GetChildren(treeView).OfType<Border>();
            Border border = logicalChildren.FirstOrDefault();

            if (border != null)
            {
                return border;
            }

            // Если не нашли в логическом, ищем в визуальном
            if (VisualTreeHelper.GetChildrenCount(treeView) > 0)
            {
                DependencyObject child = VisualTreeHelper.GetChild(treeView, 0);
                return child as Border;
            }

            return null;
        }

        private void CollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseExpandAll(false);
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseExpandAll(true);
        }

        private void CollapseExpandAll(bool expand)
        {
            if (toolboxControl == null)
            {
                return;
            }

            // Находим TreeView с именем PART_Tools
            TreeView treeView = FindVisualChildByName<TreeView>(toolboxControl, "PART_Tools");

            if (treeView == null)
            {
                return;
            }

            // Получаем все TreeViewItem и устанавливаем IsExpanded
            IEnumerable<TreeViewItem> treeViewItems = FindVisualChildren<TreeViewItem>(treeView);
            foreach (TreeViewItem item in treeViewItems)
            {
                item.IsExpanded = expand;
            }
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Name == name)
                {
                    return element;
                }

                T result = FindVisualChildByName<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null)
            {
                yield break;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);

#pragma warning disable IDE0038 // Use pattern matching
                if (child != null && child is T)
                {
                    yield return (T)child;
                }
#pragma warning restore IDE0038 // Use pattern matching

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
}
