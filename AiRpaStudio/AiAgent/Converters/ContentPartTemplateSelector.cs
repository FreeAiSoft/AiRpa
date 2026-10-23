using System.Windows;
using System.Windows.Controls;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Selects the appropriate data template based on the content part type
    /// </summary>
    public class ContentPartTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }
        public DataTemplate CodeBlockTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is MessageContentPart part)
            {
                return part.Type == ContentPartType.CodeBlock ? CodeBlockTemplate : TextTemplate;
            }

            return TextTemplate;
        }
    }
}