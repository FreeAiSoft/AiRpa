using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiAgent.Models;

namespace AiAgent.Services
{
    /// <summary>
    /// Интерфейс для хранения диалогов
    /// </summary>
    public interface IConversationStorage
    {
        /// <summary>
        /// Сохраняет диалог
        /// </summary>
        Task SaveConversationAsync(Conversation conversation);

        /// <summary>
        /// Загружает диалог по идентификатору
        /// </summary>
        Task<Conversation> LoadConversationAsync(Guid id);

        /// <summary>
        /// Получает список всех диалогов
        /// </summary>
        Task<List<Conversation>> GetAllConversationsAsync();

        /// <summary>
        /// Удаляет диалог
        /// </summary>
        Task DeleteConversationAsync(Guid id);
    }
}