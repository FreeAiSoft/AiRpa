using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Models;

namespace AiAgent.Services
{
    /// <summary>
    /// Интерфейс для работы с Ollama API
    /// </summary>
    public interface IOllamaService
    {
        /// <summary>
        /// Получает список доступных моделей
        /// </summary>
        Task<List<OllamaModel>> GetModelsAsync();

        /// <summary>
        /// Отправляет сообщения в чат и получает ответ с потоковой передачей
        /// </summary>
        /// <param name="model">Название модели</param>
        /// <param name="messages">История сообщений</param>
        /// <param name="onChunkReceived">Callback для получения частей ответа</param>
        /// <param name="cancellationToken">Токен отмены</param>
        Task ChatStreamAsync(
            string model,
            List<ChatMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Проверяет, запущен ли Ollama
        /// </summary>
        Task<bool> IsOllamaRunningAsync();
    }
}