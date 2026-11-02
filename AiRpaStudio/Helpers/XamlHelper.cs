using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    internal class XamlHelper
    {
        /// <summary>
        /// Принадлежит ли объект визуальному дереву. Дети <c>Grid</c>/<c>Panel</c>, которые отдаёт
        /// <see cref="LogicalTreeHelper.GetChildren"/>, — это не только элементы, но и
        /// <c>RowDefinition</c>/<c>ColumnDefinition</c>: <see cref="VisualTreeHelper"/> на них бросает
        /// <see cref="System.ArgumentException"/> («...не является Visual или Visual3D»).
        /// </summary>
        public static bool IsVisual(DependencyObject value)
        {
            return value is Visual;
        }

        /// <summary>
        /// Прямые дети элемента: логические (они существуют ещё до применения шаблона) и визуальные
        /// (появляются после), дубликаты отсекаются. Не-визуальные узлы (<c>RowDefinition</c>) тоже
        /// возвращаются как <see cref="DependencyObject"/> — обходить их детей нельзя, но их свойства
        /// (кисти) читать безопасно.
        /// </summary>
        public static IEnumerable<DependencyObject> Children(DependencyObject parent)
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

            if (IsVisual(parent))
            {
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int index = 0; index < count; index++)
                {
                    DependencyObject visualChild = VisualTreeHelper.GetChild(parent, index);
                    if (seen.Add(visualChild))
                    {
                        yield return visualChild;
                    }
                }
            }
        }

        /// <summary>
        /// Безопасный обход визуального дерева: не-визуальный узел (например <c>RowDefinition</c>)
        /// даёт пустую последовательность вместо исключения.
        /// </summary>
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (!IsVisual(parent))
            {
                yield break;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
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

        /// <summary>
        /// Поиск визуального потомка по типу и имени
        /// </summary>
        public static T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (!IsVisual(parent))
            {
                return null;
            }

            T foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    if (string.IsNullOrEmpty(childName))
                    {
                        foundChild = typedChild;
                        break;
                    }
                    else if (child is FrameworkElement frameworkElement &&
                            (frameworkElement.Name == childName ||
                             frameworkElement.GetType().Name == childName))
                    {
                        foundChild = typedChild;
                        break;
                    }
                }

                foundChild = FindVisualChild<T>(child, childName);
                if (foundChild != null)
                {
                    break;
                }
            }

            return foundChild;
        }

        /// <summary>
        /// Поиск логического потомка по типу и имени
        /// </summary>
        public static T FindLogicalChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            T foundChild = null;

            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is DependencyObject dependencyChild)
                {
                    if (dependencyChild is T typedChild)
                    {
                        if (string.IsNullOrEmpty(childName))
                        {
                            foundChild = typedChild;
                            break;
                        }
                        else if (dependencyChild is FrameworkElement frameworkElement &&
                                (frameworkElement.Name == childName ||
                                 frameworkElement.GetType().Name == childName))
                        {
                            foundChild = typedChild;
                            break;
                        }
                    }

                    foundChild = FindLogicalChild<T>(dependencyChild, childName);
                    if (foundChild != null)
                    {
                        break;
                    }
                }
            }

            return foundChild;
        }

        /// <summary>
        /// Поиск визуального родителя по типу
        /// </summary>
        public static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (!IsVisual(child))
            {
                // RowDefinition/ColumnDefinition и прочие логические узлы: GetParent на них бросает.
                return null;
            }

            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            return parentObject == null ? null : parentObject is T parent ? parent : FindVisualParent<T>(parentObject);
        }

        /// <summary>
        /// Поиск визуального потомка по имени типа и x:Name
        /// </summary>
        public static FrameworkElement FindVisualChildByTypeAndName(DependencyObject parent, string typeName, string elementName)
        {
            if (!IsVisual(parent))
            {
                return null;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                // Проверяем, соответствует ли тип и имя
                if (child is FrameworkElement frameworkElement &&
                    child.GetType().Name == typeName &&
                    frameworkElement.Name == elementName)
                {
                    return frameworkElement;
                }

                // Рекурсивный поиск в потомках
                FrameworkElement foundChild = FindVisualChildByTypeAndName(child, typeName, elementName);
                if (foundChild != null)
                {
                    return foundChild;
                }
            }

            return null;
        }

    }
}
