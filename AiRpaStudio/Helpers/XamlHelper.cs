using System.Windows.Media;
using System.Windows;

namespace RPA.Helpers
{
    internal class XamlHelper
    {
        /// <summary>
        /// Поиск визуального потомка по типу и имени
        /// </summary>
        public static T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null)
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
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            return parentObject == null ? null : parentObject is T parent ? parent : FindVisualParent<T>(parentObject);
        }

        /// <summary>
        /// Поиск визуального потомка по имени типа и x:Name
        /// </summary>
        public static FrameworkElement FindVisualChildByTypeAndName(DependencyObject parent, string typeName, string elementName)
        {
            if (parent == null)
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
