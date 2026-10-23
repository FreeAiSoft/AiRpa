using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Services
{
    /// <summary>
    /// Информация об использовании токенов
    /// </summary>
    public class UsageInfo
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    /// <summary>
    /// Базовый интерфейс для всех AI провайдеров
    /// </summary>
    public interface IAiProvider
    {
        /// <summary>
        /// Тип провайдера
        /// </summary>
        ProviderType Type { get; }

        /// <summary>
        /// Название провайдера для отображения
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Получает список доступных моделей
        /// </summary>
        Task<List<AiModel>> GetModelsAsync();

        /// <summary>
        /// Отправляет сообщения в чат и получает ответ с потоковой передачей
        /// </summary>
        /// <param name="model">Название/ID модели</param>
        /// <param name="messages">История сообщений</param>
        /// <param name="onChunkReceived">Callback для получения частей ответа</param>
        /// <param name="onUsageReceived">Callback для получения информации об использовании токенов (опционально)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        Task ChatStreamAsync(
            string model,
            List<ChatMessage> messages,
            Action<string> onChunkReceived,
            Action<UsageInfo> onUsageReceived = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, доступен ли провайдер
        /// </summary>
        Task<bool> IsAvailableAsync();
    }
}