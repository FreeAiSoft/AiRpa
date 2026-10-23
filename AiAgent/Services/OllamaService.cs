using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiAgent.Models;
using Newtonsoft.Json;

namespace AiAgent.Services
{
    /// <summary>
    /// Реализация сервиса для работы с Ollama API
    /// </summary>
    public class OllamaService : IOllamaService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "http://localhost:11434";

        public OllamaService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        /// <summary>
        /// Получает список доступных моделей
        /// </summary>
        public async Task<List<OllamaModel>> GetModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/tags");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonConvert.DeserializeObject<OllamaModelsResponse>(json);

                if (modelsResponse?.Models == null)
                    return new List<OllamaModel>();

                return modelsResponse.Models.Select(m => new OllamaModel
                {
                    Name = m.Name,
                    ModifiedAt = ParseDateTime(m.ModifiedAt),
                    Size = m.Size
                }).ToList();
            }
            catch (Exception ex)
            {
                throw new Exception("Не удалось получить список моделей: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Отправляет сообщения в чат и получает ответ с потоковой передачей
        /// </summary>
        public async Task ChatStreamAsync(
            string model,
            List<ChatMessage> messages,
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default)
        {
            var requestMessages = messages.Select(m => new OllamaRequestMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToArray();

            var request = new OllamaChatRequest
            {
                Model = model,
                Messages = requestMessages,
                Stream = true
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = content
            })
            {
                using (var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, true))
                    {
                        var buffer = new char[4096];
                        var lineBuffer = new StringBuilder();

                        while (!cancellationToken.IsCancellationRequested)
                        {
                            // Читаем данные асинхронно небольшими порциями
                            var readTask = reader.ReadAsync(buffer, 0, buffer.Length);
                            var bytesRead = await readTask.ConfigureAwait(false);
                            
                            if (bytesRead == 0)
                                break;

                            // Обрабатываем прочитанные данные
                            for (int i = 0; i < bytesRead; i++)
                            {
                                if (buffer[i] == '\n')
                                {
                                    var line = lineBuffer.ToString().Trim();
                                    lineBuffer.Clear();

                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    // Парсим JSON и вызываем callback
                                    try
                                    {
                                        var ollamaResponse = JsonConvert.DeserializeObject<OllamaResponse>(line);
                                        
                                        if (ollamaResponse?.Message?.Content != null)
                                        {
                                            onChunkReceived?.Invoke(ollamaResponse.Message.Content);
                                        }

                                        if (ollamaResponse?.Done == true)
                                        {
                                            return;
                                        }
                                    }
                                    catch
                                    {
                                        // Игнорируем ошибки парсинга
                                    }
                                }
                                else
                                {
                                    lineBuffer.Append(buffer[i]);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Проверяет, запущен ли Ollama
        /// </summary>
        public async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private DateTime ParseDateTime(string dateTimeString)
        {
            if (DateTime.TryParse(dateTimeString, out var result))
                return result;
            return DateTime.Now;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}