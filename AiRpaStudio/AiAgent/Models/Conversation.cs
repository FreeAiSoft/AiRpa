using System;
using System.Collections.ObjectModel;

namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Представляет диалог с AI
    /// </summary>
    public class Conversation
    {
        /// <summary>
        /// Уникальный идентификатор диалога
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Название диалога
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Дата создания
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Дата последнего обновления
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Название используемой модели
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// Коллекция сообщений в диалоге
        /// </summary>
        public ObservableCollection<ChatMessage> Messages { get; set; }

        public Conversation()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
            Messages = new ObservableCollection<ChatMessage>();
            Title = "Новый диалог";
        }
    }
}
