using System.Windows;
using System.Windows.Controls;
using RPA.Locals;

namespace RPA.Controls
{
    public class LocalsNodeTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ScalarTemplate { get; set; }

        public DataTemplate TreeTemplate { get; set; }

        public DataTemplate TableTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is LocalsNode node)
            {
                if (node.Kind == LocalsNodeKind.Table)
                {
                    return TableTemplate;
                }

                if (node.Kind == LocalsNodeKind.Object
                    || node.Kind == LocalsNodeKind.Collection
                    || node.Kind == LocalsNodeKind.Dictionary)
                {
                    return TreeTemplate;
                }
            }

            return ScalarTemplate;
        }
    }
}
