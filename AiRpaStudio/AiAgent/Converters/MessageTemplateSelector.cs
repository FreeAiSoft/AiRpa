using System.Windows;
using System.Windows.Controls;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Селектор шаблона для сообщений в зависимости от роли и типа
    /// </summary>
    public class MessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate MessageTemplate { get; set; }
        public DataTemplate SystemMessageTemplate { get; set; }
        public DataTemplate McpCommandTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is ChatMessage message)
            {
                // Используем специальный шаблон для MCP команд
                if (message.Type == MessageType.McpCommand)
                {
                    return McpCommandTemplate;
                }
                
                // Используем специальный шаблон для системных сообщений
                if (message.Type == MessageType.System || message.Role == "system")
                {
                    return SystemMessageTemplate;
                }
            }

            // Для остальных используем стандартный шаблон
            return MessageTemplate;
        }
    }
}